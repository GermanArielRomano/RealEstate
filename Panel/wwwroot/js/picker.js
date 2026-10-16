// Selector de ubicación (Leaflet + OpenStreetMap + Nominatim). Blazor lo maneja por interop: ver LocationPicker.razor
(function () {
    const round = n => Math.round(n * 1e6) / 1e6;

    window.rePicker = {
        _s: {},

        init(id, dotnet, lat, lon) {
            if (this._s[id] || !document.getElementById(id)) return;

            const hasPos = lat != null && lon != null;
            const map = L.map(id).setView(hasPos ? [lat, lon] : [-34.6, -61.0], hasPos ? 16 : 6);
            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                { maxZoom: 19, attribution: '&copy; OpenStreetMap' }).addTo(map);

            const s = { map, dotnet, marker: null };
            this._s[id] = s;

            map.on('click', e => this._place(id, e.latlng.lat, e.latlng.lng, true));
            if (hasPos) this._place(id, lat, lon, false);
            setTimeout(() => map.invalidateSize(), 200);
        },

        _place(id, lat, lon, notify) {
            const s = this._s[id];
            if (!s) return;

            if (!s.marker) {
                s.marker = L.marker([lat, lon], { draggable: true }).addTo(s.map);
                s.marker.on('dragend', () => {
                    const p = s.marker.getLatLng();
                    s.dotnet.invokeMethodAsync('OnPinMoved', round(p.lat), round(p.lng));
                });
            } else {
                s.marker.setLatLng([lat, lon]);
            }

            if (notify) s.dotnet.invokeMethodAsync('OnPinMoved', round(lat), round(lon));
        },

        // Prueba cada consulta en orden (dirección completa, luego solo la ciudad). Devuelve null si ninguna da resultado.
        async geocode(id, queries) {
            for (let i = 0; i < queries.length; i++) {
                try {
                    const url = 'https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=ar&q='
                        + encodeURIComponent(queries[i]);
                    const res = await fetch(url, { headers: { 'Accept-Language': 'es' } });
                    if (!res.ok) continue;
                    const data = await res.json();
                    if (data.length) {
                        const lat = round(parseFloat(data[0].lat)), lon = round(parseFloat(data[0].lon));
                        const s = this._s[id];
                        if (s) {
                            this._place(id, lat, lon, false);
                            s.map.setView([lat, lon], i === 0 ? 17 : 14);
                        }
                        return { lat, lon, approximate: i > 0 };
                    }
                } catch (e) { /* se prueba la siguiente consulta */ }
            }
            return null;
        },

        clear(id) {
            const s = this._s[id];
            if (s && s.marker) { s.map.removeLayer(s.marker); s.marker = null; }
        },

        dispose(id) {
            const s = this._s[id];
            if (s) { s.map.remove(); delete this._s[id]; }
        }
    };
})();
