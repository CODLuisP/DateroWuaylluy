# DateroWuaylluy (`datero-wuaylluy`)

Servicio de control de despachos, monitoreo GPS por SignalR y cambio automático de sentido para unidades de transporte urbano (.NET 10).

---

## Requisitos en el servidor (Ubuntu / Droplet 28)

- Docker y `docker-compose` (v1.29.2)
- Red externa de Docker `app-net` (donde corre el contenedor `mysql-gts`):
  ```bash
  docker network inspect app-net >/dev/null 2>&1 || docker network create app-net
  ```

---

## Instalación

1. Clonar el repositorio:
   ```bash
   git clone https://github.com/CODLuisP/DateroWuaylluy.git
   cd DateroWuaylluy
   ```

2. Crear el archivo `appsettings.json` a partir de la plantilla `appsettings.example.json` y colocar las credenciales reales:
   ```bash
   cp appsettings.example.json appsettings.json
   nano appsettings.json
   ```
   > **Nota:** `appsettings.json` no está versionado en Git y se monta en modo solo lectura (`:ro`) dentro del contenedor en `/app/appsettings.json`. El puerto interno debe mantenerse en `8080`.

3. Construir y levantar el contenedor:
   ```bash
   docker-compose build
   docker-compose up -d
   ```

---

## Actualización

Para actualizar el servicio cuando haya nuevos cambios en la rama `main`:

```bash
git pull && docker-compose build && docker-compose down && docker-compose up -d
```

---

## Comprobar que arrancó correctamente

1. Verificar el estado del contenedor y su `HEALTHCHECK`:
   ```bash
   docker-compose ps
   ```

2. Consultar el endpoint de salud (expuesto en loopback `127.0.0.1:5004`):
   ```bash
   curl -i http://127.0.0.1:5004/health
   ```
   Respuesta esperada:
   ```json
   {"status":"running","timestamp":"2026-10-25T21:00:00.0000000-05:00"}
   ```

3. Revisar los logs en tiempo real:
   ```bash
   docker-compose logs -f --tail=100
   ```

---

## Ejemplo de configuración en Nginx (Reverse Proxy)

El contenedor publica únicamente en `127.0.0.1:5004` hacia el puerto `8080` interno. Ejemplo de bloque `server` / `location` para Nginx:

```nginx
server {
    listen 80;
    server_name datero-wuaylluy.tudominio.com;

    location / {
        proxy_pass http://127.0.0.1:5004;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }
}
```
