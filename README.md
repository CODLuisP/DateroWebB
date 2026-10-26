# DateroWebB

Sistema de control de tiempos de paraderos en `.NET 10` que se conecta por SignalR a `https://villa.velsat.pe:8443` y expone un servidor HTTP (`HttpListener`) en el puerto `5003` (`/api/iniciar-monitoreo` y `/health`).

---

## Instalación con Docker

Asegúrate de que exista la red externa `app-net` en el servidor (si aún no está creada, ejecuta `docker network create app-net`).

```bash
git clone https://github.com/CODLuisP/DateroWebB.git
cd DateroWebB
docker-compose build
docker-compose up -d
```

El servicio queda expuesto localmente en el servidor en `http://127.0.0.1:3013` (redirigido al puerto interno `5003` del contenedor).

---

## Actualización

Para actualizar el contenedor con los últimos cambios del repositorio:

```bash
git pull && docker-compose build && docker-compose down && docker-compose up -d
```

---

## Ver logs

Para visualizar los registros del contenedor en tiempo real:

```bash
docker logs -f datero-web-b
```

---

## ¿Qué es el Modo Servicio?

Por defecto, cuando `DateroWebB` se ejecuta en una consola interactiva (por ejemplo, en Windows durante desarrollo), muestra un menú interactivo (`MenuPrincipal`) que espera entrada de teclado mediante `Console.ReadLine()` y `Console.ReadKey()`.

En entornos desatendidos como **Docker**, no existe entrada estándar interactiva. El **Modo Servicio** se activa automáticamente cuando:
- La variable de entorno `MODO_SERVICIO=true` está definida, o
- `Console.IsInputRedirected` es `true`.

Cuando está en **Modo Servicio**:
1. **No muestra el menú interactivo** ni invoca `Console.ReadLine()` o `Console.ReadKey()`.
2. Mantiene el proceso en ejecución continua atendiendo peticiones HTTP y eventos de SignalR.
3. **Cada 10 minutos** imprime en consola una línea de estado indicando si el Hub de Despacho está conectado/desconectado y la cantidad de monitoreos activos.
4. Al recibir **`SIGTERM`** (por ejemplo con `docker stop` o `docker-compose down`) o **`Ctrl+C`**, realiza un apagado limpio deteniendo el servidor HTTP, desconectando todos los monitoreos GPS activos y cerrando la conexión al Hub de Despachos antes de finalizar el proceso.
