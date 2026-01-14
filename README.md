# DaemonQueueQuery

Servicio worker en .NET que ejecuta un stored procedure de SQL Server en un bucle.

## Requisitos

- .NET SDK 10.0
- SQL Server accesible

## Configuracion

Se usa `appsettings.json`/`appsettings.Development.json`:

- `DaemonQueueQuery:ProcedureName`: nombre del stored procedure
- `ConnectionStrings:DefaultConnection`: cadena de conexion a SQL Server

Ejemplo:

```
{
  "DaemonQueueQuery": {
    "ProcedureName": "dbo.usp_ProcesarSiguienteItem"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=plataformas1;Database=master;User ID=tu_usuario;Password=tu_contrasena;TrustServerCertificate=True;"
  }
}
```

## Ejecutar local

```
dotnet run
```

## Publicar en Windows

Publicar como ejecutable en una carpeta local:

```
dotnet publish -c Release -r win-x64 -o C:\\Services\\DaemonQueueQuery
```

Si prefieres portable (requiere .NET instalado):

```
dotnet publish -c Release -o C:\\Services\\DaemonQueueQuery
```

Luego instala el servicio con `deploy/install-windows-service.ps1`.

## Generar aplicacion de produccion

Compilacion recomendada (Release):

```
dotnet publish -c Release
```

Opcionalmente, generar un binario self-contained:

```
dotnet publish -c Release -r win-x64 --self-contained true
```

Para Linux:

```
dotnet publish -c Release -r linux-x64 --self-contained true
```

## Docker

Build y run:

```
docker build -t daemon-queue-query .
docker run --rm \
  -e DOTNET_ENVIRONMENT=Development \
  -e ConnectionStrings__DefaultConnection="Server=plataformas1;Database=master;User ID=tu_usuario;Password=tu_contrasena;TrustServerCertificate=True;" \
  daemon-queue-query
```

Con docker compose (incluye SQL Server local):

```
docker compose up --build
```

## Linux (systemd)

1) Publicar la app:

```
dotnet publish -c Release -o /opt/daemonqueuequery
```

2) Copiar la unidad:

```
sudo cp deploy/daemonqueuequery.service /etc/systemd/system/daemonqueuequery.service
```

3) Ajustar `WorkingDirectory`, `ExecStart` y variables si aplica.

4) Habilitar e iniciar:

```
sudo systemctl daemon-reload
sudo systemctl enable --now daemonqueuequery
```

## Windows Service

Publicar la app (carpeta de instalacion a tu eleccion) y ejecutar el script:

```
PowerShell -ExecutionPolicy Bypass -File deploy\install-windows-service.ps1 \
  -ServiceName DaemonQueueQuery \
  -InstallPath C:\\Services\\DaemonQueueQuery \
  -Environment Production \
  -ConnectionString "Server=plataformas1;Database=master;User ID=tu_usuario;Password=tu_contrasena;TrustServerCertificate=True;"
```

Para iniciar:

```
sc.exe start DaemonQueueQuery
```

## Archivos relevantes

- `Program.cs`: host y registro de servicio.
- `Worker.cs`: ejecucion del stored procedure.
- `Dockerfile`: imagen de runtime.
- `docker-compose.yml`: worker + SQL Server local.
- `deploy/daemonqueuequery.service`: unidad systemd.
- `deploy/install-windows-service.ps1`: instalador del servicio en Windows.

## para publicar en windows
makeruntime.bat
y luego ejecutar el installservice.bat