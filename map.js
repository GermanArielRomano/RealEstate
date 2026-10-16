// Mapa de búsqueda (Leaflet + OpenStreetMap). Blazor lo maneja por interop: ver PropertyMap.razor
(function () {
    const esc = t => String(t ?? '').replace(/[&<>"']/g,
        c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    window.reMap = {
        _s: {},

        init(id) {
            if (this._s[id] || !document.getElementById(id)) return;

            const saved = localStorage.getItem('re_theme');
            const dark = (saved ?? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')) === 'dark';

            const map = L.map(id).setView([-34.6, -61.0], 6);
            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                { maxZoom: 19, attribution: '&copy; OpenStreetMap' }).addTo(map);
            if (dark) map.getContainer().classList.add('re-map-dark');

            this._s[id] = { map, layer: L.layerGroup().addTo(map) };
            setTimeout(() => map.invalidateSize(), 200);
        },

        // items: [{ id, lat, lon, label, title, sub, href }]
        setMarkers(id, items, fit) {
            const s = this._s[id];
            if (!s) return;

            s.layer.clearLayers();
            const points = [];
            items.forEach(i => {
                const icon = L.divIcon({
                    className: 're-pin-wrap',
                    html: `<div class="re-pin">${esc(i.label)}</div>`,
                    iconSize: [0, 0]
                });
                L.marker([i.lat, i.lon], { icon })
                    .bindPopup(`<div class="re-pop"><b>${esc(i.title)}</b><br>${esc(i.sub)}<br><a href="${esc(i.href)}">Ver detalle</a></div>`)
                    .addTo(s.layer);
                points.push([i.lat, i.lon]);
            });

            if (fit && points.length) s.map.fitBounds(points, { padding: [48, 48], maxZoom: 16 });
        },

        // [sur, oeste, norte, este] del área visible
        getBounds(id) {
            const s = this._s[id];
            if (!s) return null;
            const b = s.map.getBounds();
            return [b.getSouth(), b.getWest(), b.getNorth(), b.getEast()];
        },

        dispose(id) {
            const s = this._s[id];
            if (s) { s.map.remove(); delete this._s[id]; }
        }
    };
})();
