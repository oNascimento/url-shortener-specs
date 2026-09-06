import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import './style.css';
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
createRoot(document.getElementById('root')!).render(
  <StrictMode><QueryClientProvider client={queryClient}><BrowserRouter>
    <main><p>ENCURTADOR DE URLS</p><h1>Seus links, em um só lugar.</h1>
    <p>A base da aplicação está pronta. Cadastro e gestão de links serão disponibilizados nas próximas entregas.</p></main>
  </BrowserRouter></QueryClientProvider></StrictMode>,
);
