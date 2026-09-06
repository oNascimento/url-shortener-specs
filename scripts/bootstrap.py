"""Create local secrets once; never overwrite an existing environment."""
from pathlib import Path
import secrets
root = Path(__file__).resolve().parent.parent
target = root / '.env'
if not target.exists():
    with target.open('x', encoding='utf-8') as stream:
        for name in ('POSTGRES_PASSWORD', 'REGISTRY_PASSWORD', 'RABBITMQ_PASSWORD', 'GRAFANA_PASSWORD'):
            stream.write(f'{name}={secrets.token_hex(24)}\n')
print('Local environment ready. Secrets are in ignored .env; do not commit or paste them.')
