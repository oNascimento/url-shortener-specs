"""Check the local foundation, without credentials or external destinations."""
import json
import http.client
import socket
import subprocess
import ssl
import time
import urllib.parse
import urllib.request
import urllib.error

def get(url, *, local_tls=False):
    if local_tls:
        parsed = urllib.parse.urlsplit(url)
        assert parsed.hostname in ('localhost', 's.localhost')
        # Exercise the real SNI/Host routing without changing the machine's hosts file.
        class LocalConnection(http.client.HTTPSConnection):
            def connect(self):
                sock = socket.create_connection(('127.0.0.1', 443), timeout=5)
                self.sock = self._context.wrap_socket(sock, server_hostname=self.host)
        connection = LocalConnection(parsed.hostname, context=ssl._create_unverified_context(), timeout=5)
        try:
            connection.request('GET', parsed.path or '/')
            response = connection.getresponse()
            body = response.read().decode()
            if response.status >= 400:
                raise urllib.error.HTTPError(url, response.status, response.reason, response.headers, None)
            return body
        finally:
            connection.close()
    with urllib.request.urlopen(url, timeout=5) as response:
        return response.read().decode()

deadline = time.monotonic() + 90
while True:
    try:
        query = urllib.parse.urlencode({'query': '{service_name=~"shortener-.+"}', 'limit': 100})
        result = json.loads(get('http://localhost:3100/loki/api/v1/query_range?' + query))
        services = {item['stream']['service_name'] for item in result['data']['result']}
        assert {'shortener-api', 'shortener-redirector', 'shortener-worker', 'shortener-jobs'} <= services
        assert json.loads(get('http://localhost:3000/api/health'))['database'] == 'ok'
        assert get('http://localhost:9090/-/ready')
        assert 'root' in get('https://localhost', local_tls=True)
        for service in ('api', 'redirector', 'worker', 'jobs'):
            probe = subprocess.run(['docker', 'compose', 'exec', '-T', 'proxy', 'wget', '-qO-',
                                    f'http://{service}:8080/health/ready'], capture_output=True, text=True, timeout=10)
            assert probe.returncode == 0 and json.loads(probe.stdout)['status'] == 'ready'
        metric = json.loads(get('http://localhost:9090/api/v1/query?query=shortener_requests_total'))
        assert metric['data']['result'], 'Application metrics did not reach Prometheus'
        for origin in ('https://localhost', 'https://s.localhost'):
            try:
                get(origin + '/health/live', local_tls=True)
                raise AssertionError('Operational endpoint exposed through public proxy')
            except urllib.error.HTTPError as error:
                assert error.code == 404
        break
    except (AssertionError, OSError, ValueError, KeyError, subprocess.TimeoutExpired):
        if time.monotonic() >= deadline:
            raise
        time.sleep(1)
print('PASS: four services ready, logs in Loki, application metrics in Prometheus, Grafana, HTTPS and private health routes.')
