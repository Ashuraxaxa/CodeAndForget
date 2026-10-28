import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'
import { isSchemaChangedError } from './api/client'
import { I18nProvider } from './i18n'
import '@fontsource-variable/inter'
import './index.css'

// La tabla cambió en la base (P-086): el servidor ya descartó el esquema viejo; se vuelve a pedir la definición.
const onError = (err: unknown) => {
  if (!isSchemaChangedError(err)) return
  for (const key of ['meta', 'tables', 'diff', 'override-initial']) queryClient.invalidateQueries({ queryKey: [key] })
}

const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: {
    queries: { retry: false, refetchOnWindowFocus: false },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <QueryClientProvider client={queryClient}>
        <App />
      </QueryClientProvider>
    </I18nProvider>
  </StrictMode>,
)
