// Service worker mínimo: hace la app instalable. NO guarda nada en caché a propósito:
// así, tras cada despliegue el comprador siempre recibe la versión nueva (sin contenido viejo).
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { });
