// Mapa de búsqueda (Leaflet + OpenStreetMap + markercluster). Blazor lo maneja por interop: ver PropertyMap.razor
(function () {
    const MAX_VERTICES = 60;
    const round = n => Math.round(n * 1e6) / 1e6;
    const esc = t => String(t ?? '').replace(/[&<>"']/g,
        c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    // ---- Simplificación de trazos (Ramer–Douglas–Peucker) sobre puntos de pantalla ----
    function perpDist(p, a, b) {
        const dx = b.x - a.x, dy = b.y - a.y, len = Math.hypot(dx, dy);
        if (!len) return p.distanceTo(a);
        return Math.abs(dy * p.x - dx * p.y + b.x * a.y - b.y * a.x) / len;
    }
    function rdp(pts, eps) {
        if (pts.length < 3) return pts;
        let max = 0, idx = 0;
        for (let i = 1; i < pts.length - 1; i++) {
            const d = perpDist(pts[i], pts[0], pts[pts.length - 1]);
            if (d > max) { max = d; idx = i; }
        }
        if (max > eps) {
            const left = rdp(pts.slice(0, idx + 1), eps), right = rdp(pts.slice(idx), eps);
            return left.slice(0, -1).concat(right);
        }
        return [pts[0], pts[pts.length - 1]];
    }

    window.reMap = {
        _s: {},

        init(id, dotnet) {
            if (this._s[id] || !document.getElementById(id)) return;

            const saved = localStorage.getItem('re_theme');
            const dark = (saved ?? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')) === 'dark';

            const map = L.map(id).setView([-34.6, -61.0], 6);
            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                { maxZoom: 19, attribution: '&copy; OpenStreetMap' }).addTo(map);
            if (dark) map.getContainer().classList.add('re-map-dark');

            // Pines agrupados: al acercar se separan; los muy juntos se abanican
            const layer = L.markerClusterGroup({
                showCoverageOnHover: false,
                maxClusterRadius: 55,
                iconCreateFunction: c => L.divIcon({
                    html: `<div class="re-cluster">${c.getChildCount()}</div>`,
                    className: 're-cluster-wrap',
                    iconSize: [40, 40]
                })
            });
            map.addLayer(layer);

            this._s[id] = { map, layer, dotnet, zone: null, me: null, draw: null };
            setTimeout(() => map.invalidateSize(), 200);
        },

        // items: [{ id, lat, lon, label, title, sub, href }]
        setMarkers(id, items, mode) {
            const s = this._s[id];
            if (!s) return;

            s.layer.clearLayers();
            const markers = [], points = [];
            items.forEach(i => {
                const icon = L.divIcon({
                    className: 're-pin-wrap',
                    html: `<div class="re-pin">${esc(i.label)}</div>`,
                    iconSize: [0, 0]
                });
                markers.push(L.marker([i.lat, i.lon], { icon })
                    .bindPopup(`<div class="re-pop"><b>${esc(i.title)}</b><br>${esc(i.sub)}<br><a href="${esc(i.href)}">Ver detalle</a></div>`));
                points.push([i.lat, i.lon]);
            });
            s.layer.addLayers(markers);

            // Con zona activa se encuadra la zona; sin zona, los pines; con "none" se respeta la vista actual
            if (mode === 'zone' && s.zone) this._fitZone(id);
            else if (mode === 'points' && points.length) s.map.fitBounds(points, { padding: [48, 48], maxZoom: 16 });
        },

        // Centra y agranda el mapa sobre la zona. Sin animación: así no choca con la carga de pines agrupados.
        _fitZone(id) {
            const s = this._s[id];
            if (!s || !s.zone) return;
            s.map.invalidateSize();
            s.map.fitBounds(s.zone.getBounds(), { padding: [32, 32], maxZoom: 17, animate: false });
        },

        // [sur, oeste, norte, este] del área visible
        getBounds(id) {
            const s = this._s[id];
            if (!s) return null;
            const b = s.map.getBounds();
            return [b.getSouth(), b.getWest(), b.getNorth(), b.getEast()];
        },

        // ---------------- Zona (polígono) ----------------
        // latlngs: [[lat, lon], ...] o null para quitarla
        setZone(id, latlngs, fit) {
            const s = this._s[id];
            if (!s) return;

            if (s.zone) { s.map.removeLayer(s.zone); s.zone = null; }
            if (!latlngs || latlngs.length < 3) return;

            s.zone = L.polygon(latlngs, { color: '#3b6fe0', weight: 2, fillColor: '#3b6fe0', fillOpacity: 0.08 }).addTo(s.map);
            if (fit) this._fitZone(id);
        },

        // ---------------- Dibujo a mano alzada ----------------
        startDraw(id) {
            const s = this._s[id];
            if (!s) return;
            this.cancelDraw(id);

            const map = s.map, el = map.getContainer();
            map.dragging.disable(); map.touchZoom.disable(); map.doubleClickZoom.disable();
            map.scrollWheelZoom.disable(); map.boxZoom.disable();
            el.style.cursor = 'crosshair';
            el.style.touchAction = 'none';   // en el celular, el dedo dibuja en lugar de desplazar la página

            let pts = [], line = null, active = false;
            const toPoint = e => map.mouseEventToContainerPoint(e);

            const down = e => {
                if (e.target.closest && e.target.closest('.leaflet-control')) return;   // no dibujar sobre los botones de zoom
                active = true;
                pts = [toPoint(e)];
                if (line) map.removeLayer(line);
                line = L.polyline([map.containerPointToLatLng(pts[0])], { color: '#3b6fe0', weight: 3 }).addTo(map);
                if (el.setPointerCapture) el.setPointerCapture(e.pointerId);
                e.preventDefault();
            };
            const move = e => {
                if (!active) return;
                const p = toPoint(e);
                if (p.distanceTo(pts[pts.length - 1]) < 6) return;
                pts.push(p);
                line.addLatLng(map.containerPointToLatLng(p));
                e.preventDefault();
            };
            const up = () => {
                if (!active) return;
                active = false;
                this._finishDraw(id, pts);
            };

            el.addEventListener('pointerdown', down);
            el.addEventListener('pointermove', move);
            el.addEventListener('pointerup', up);
            el.addEventListener('pointercancel', up);
            s.draw = { el, down, move, up, getLine: () => line };
        },

        cancelDraw(id) {
            const s = this._s[id];
            if (!s || !s.draw) return;

            const { el, down, move, up, getLine } = s.draw;
            el.removeEventListener('pointerdown', down);
            el.removeEventListener('pointermove', move);
            el.removeEventListener('pointerup', up);
            el.removeEventListener('pointercancel', up);

            const line = getLine();
            if (line) s.map.removeLayer(line);

            s.map.dragging.enable(); s.map.touchZoom.enable(); s.map.doubleClickZoom.enable();
            s.map.scrollWheelZoom.enable(); s.map.boxZoom.enable();
            el.style.cursor = ''; el.style.touchAction = '';
            s.draw = null;
        },

        _finishDraw(id, pts) {
            const s = this._s[id];
            if (!s) return;
            this.cancelDraw(id);

            // Descarta trazos diminutos o accidentales (un toque, una línea corta)
            let ok = pts.length >= 8;
            if (ok) {
                const xs = pts.map(p => p.x), ys = pts.map(p => p.y);
                ok = (Math.max(...xs) - Math.min(...xs)) > 24 && (Math.max(...ys) - Math.min(...ys)) > 24;
            }

            if (ok) {
                let eps = 2, simple = rdp(pts, eps);
                while (simple.length > MAX_VERTICES) { eps *= 1.5; simple = rdp(pts, eps); }

                if (simple.length >= 3) {
                    const verts = simple.map(p => {
                        const ll = s.map.containerPointToLatLng(p);
                        return [round(ll.lat), round(ll.lng)];
                    });
                    this.setZone(id, verts, true);
                    s.dotnet.invokeMethodAsync('OnPolygonDrawn', verts);
                }
            }
            s.dotnet.invokeMethodAsync('OnDrawEnded');
        },

        // ---------------- Cerca de mí ----------------
        locate() {
            return new Promise(resolve => {
                if (!navigator.geolocation) return resolve({ error: 'unavailable' });
                navigator.geolocation.getCurrentPosition(
                    p => resolve({ lat: p.coords.latitude, lon: p.coords.longitude }),
                    e => resolve({ error: e.code === 1 ? 'denied' : 'unavailable' }),
                    { enableHighAccuracy: false, timeout: 10000, maximumAge: 60000 });
            });
        },

        showMe(id, lat, lon) {
            const s = this._s[id];
            if (!s) return;
            if (s.me) s.map.removeLayer(s.me);
            s.me = L.circleMarker([lat, lon], { radius: 8, color: '#fff', weight: 3, fillColor: '#3b6fe0', fillOpacity: 1 }).addTo(s.map);
        },

        dispose(id) {
            const s = this._s[id];
            if (!s) return;
            this.cancelDraw(id);
            s.map.remove();
            delete this._s[id];
        }
    };
})();
