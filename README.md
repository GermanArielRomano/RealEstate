# Inmobiliarias multi-tenant — demo a costo $0

```
Client/   Blazor WASM para Clientes finales/Público     -> GitHub Pages
Panel/    Blazor WebAssembly (SuperAdmin + socios)      -> GitHub Pages
Api/      ASP.NET Core 10 + EF Core + PostgreSQL        -> Render (Docker, plan Free)
                                                        -> BD y fotos en Supabase (plan Free)
```

| Rol | Entra por | Qué ve |
|---|---|---|
| **SuperAdmin** (tú) | Panel | Alta/baja de inmobiliarias, usuarios, reseteo de claves |
| **TenantAdmin** (socio) | Panel y app | Solo las propiedades y fotos de **su** inmobiliaria |
| **Agent** | Panel y app | Solo las propiedades que tiene asignadas |

El aislamiento entre inmobiliarias lo garantiza el `tenant_id` dentro del JWT + filtros globales de EF Core.

---

## 0. Generar la migración (una sola vez, en tu PC)

No se incluye porque se genera con el SDK de .NET. No necesita base de datos ni variables de entorno:

```bash
dotnet tool install --global dotnet-ef
cd Api
dotnet ef migrations add Initial -o Infrastructure/Migrations
```

Haz commit de la carpeta `Infrastructure/Migrations`. La API aplica las migraciones sola al arrancar.
(Si la olvidas, arranca con `EnsureCreated` y deja un aviso en el log; sirve para una demo, pero no mezcles ambos métodos después.)

## 1. Supabase (BD + fotos)

1. Crea un proyecto en supabase.com, región **East US (North Virginia)**. Guarda la contraseña de la BD.
2. **Connect → Session pooler** (modo sesión, puerto 5432; compatible con IPv4). Arma la cadena en formato Npgsql:
   ```
   Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<REF_DEL_PROYECTO>;Password=<TU_CLAVE>;SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=10
   ```
   (usa el host exacto que te muestra el panel). Esto va en `ConnectionStrings__Default`.
3. **Storage → New bucket**: nombre `property-images`, marcado como **Public**.
4. **Settings → API Keys**: crea la **secret key** (`sb_secret_...`) → `Storage__SupabaseKey`. La URL del proyecto (`https://<ref>.supabase.co`) → `Storage__SupabaseUrl`. **Nunca** uses la publishable/anon ni pongas la secret en el repo, el panel o la app móvil.
5. **Seguridad multi-tenant — importante.** Supabase puede exponer las tablas por su propia API REST con la clave pública, y esa vía NO pasa por los filtros de tenant de EF. Después del primer arranque de la API (cuando ya existen las tablas), en **SQL Editor** ejecuta:
   ```sql
   DO $$ DECLARE r record; BEGIN
     FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
       EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.tablename);
     END LOOP;
   END $$;
   ```
   Activa RLS sin políticas: la API REST de Supabase no devuelve nada, y tu API (rol `postgres`) no se ve afectada. Repítelo cuando agregues tablas nuevas. Revisa también en el panel de Supabase si la Data API puede desactivarse por completo.

## 2. Render (API)

1. Sube este repositorio a GitHub (**público**: GitHub Pages gratis lo requiere).
2. En render.com: **New → Blueprint** → elige el repo. Lee `render.yaml` y te pide los valores `sync: false`:

   | Variable | Valor |
   |---|---|
   | `ConnectionStrings__Default` | cadena del paso 1.2 |
   | `Storage__SupabaseUrl` / `Storage__SupabaseKey` | pasos 1.4 |
   | `Seed__SuperAdminEmail` / `Seed__SuperAdminPassword` | **tus** credenciales (mín. 8, mayúscula, minúscula y número) |
   | `Seed__DemoPassword` | clave del socio de demo (`socio@demo.test`); si no quieres datos demo, `Seed__DemoData=false` |
   | `Cors__Origins__0` | déjala vacía por ahora (paso 3) |

3. Cuando termine el deploy, comprueba `https://<tu-servicio>.onrender.com/health` y `/health/db`.
4. Ejecuta el SQL de RLS del paso 1.5.

## 3. Panel (GitHub Pages)

1. Repo → **Settings → Pages → Source: GitHub Actions**.
2. **Settings → Secrets and variables → Actions → Variables** → `API_BASE_URL` = `https://<tu-servicio>.onrender.com/` (con `/` final).
3. Haz push a `main` (o ejecuta el workflow a mano). El panel queda en `https://<usuario>.github.io/<repo>/`.
4. En Render, define `Cors__Origins__0` = `https://<usuario>.github.io` (solo el origen, sin la ruta del repo). Render reinicia el servicio.

## 4. App móvil

En `MauiProgram.cs` pon `apiBaseUrl = "https://<tu-servicio>.onrender.com/"` (ver el bloque de comentarios al final de `Mobile/Services/PropertiesService.cs`). Render sirve HTTPS válido: no hay que configurar certificados.

---

## Límites del plan gratis que debes conocer

- **Render se duerme a los 15 min sin tráfico** y tarda cerca de 1 minuto en despertar. La app y el panel ya toleran la espera (timeouts de 90–100 s y mensajes). **Antes de una demo, abre `/health` 1–2 minutos antes.** Un monitor externo gratuito pegándole a `/health` cada ~10 min lo mantiene despierto, pero consume casi todas las 750 horas gratuitas del mes.
- **Supabase pausa el proyecto tras ~1 semana sin actividad.** Si la API arranca y `/health/db` falla, reactívalo desde su panel.
- **Render Free: 512 MB de RAM y 0,1 CPU.** Por eso `appsettings.json` limita a 4 fotos por solicitud, 20 MP por foto y 10 MB por archivo; cada foto puede tardar varios segundos en procesarse.
- **Supabase Storage: 1 GB.** Con WebP de 1600 px + miniatura, unas 3.000–5.000 fotos (estimación).
- **iOS:** repartir la app MAUI a iPhones de terceros requiere cuenta de Apple Developer (de pago). El APK de Android es gratis.
- **Desactivar una inmobiliaria o un usuario** bloquea nuevos logins y altas, pero los tokens ya emitidos siguen válidos hasta que caducan (12 h).
- El JWT del panel se guarda en `localStorage`: aceptable para la demo, no para producción.

## Desarrollo local

```bash
cd Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "<cadena de Supabase o Postgres local>"
dotnet user-secrets set "Jwt:Key" "<32+ caracteres aleatorios>"
dotnet user-secrets set "Storage:Provider" "Local"
dotnet user-secrets set "Storage:PublicBaseUrl" "https://localhost:7001/media"
dotnet user-secrets set "Seed:SuperAdminEmail" "tu@correo.com"
dotnet user-secrets set "Seed:SuperAdminPassword" "TuClave123"
dotnet user-secrets set "Cors:Origins:0" "https://localhost:7002"   # puerto del panel (ver Panel/Properties/launchSettings.json)
dotnet run
```

## No incluido (siguientes pasos naturales)

Refresh tokens, alta de agentes por parte del socio, recuperación de contraseña por correo, módulo de contactos/leads/visitas, tests de integración del aislamiento entre inmobiliarias, y pantalla de detalle + login en la app MAUI.
