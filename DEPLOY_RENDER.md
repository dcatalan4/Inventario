# Guía de Deploy en Render con Docker

## 🐳 Preparación para Deploy

### 1. Archivos Creados:
- ✅ `Dockerfile` - Configuración del contenedor
- ✅ `.dockerignore` - Archivos a excluir
- ✅ `docker-compose.yml` - Para desarrollo local
- ✅ `HealthController.cs` - Health check endpoint
- ✅ `DEPLOY_RENDER.md` - Esta guía

### 2. Configuración de appsettings.json para Producción:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "ControlInventarioContext": "Host=your-postgres-host;Port=5432;Database=control_inventario;Username=control_inventario_user;Password=control_inventario_password;SSL Mode=Require;Trust Server Certificate=true"
  }
}
```

## 🚀 Pasos para Deploy en Render

### Paso 1: Preparar el Repositorio
```bash
# 1. Agregar archivos al git
git add Dockerfile .dockerignore docker-compose.yml Controllers/HealthController.cs DEPLOY_RENDER.md
git commit -m "Add Docker configuration for Render deployment"

# 2. Push al repositorio
git push origin main
```

### Paso 2: Configurar PostgreSQL en Render
1. **Crear PostgreSQL Service:**
   - Ve a Render Dashboard
   - Click "New +" → "PostgreSQL"
   - Nombre: `control-inventario-db`
   - Database Name: `control_inventario`
   - User: `control_inventario_user`
   - Password: (generado automáticamente)
   - Region: elige la más cercana a tus usuarios

2. **Obtener Connection String:**
   - Una vez creado, ve al servicio
   - Copia el "External Database URL"
   - Formato: `postgresql://user:password@host:port/database`

### Paso 3: Crear Web Service en Render
1. **Crear Web Service:**
   - Click "New +" → "Web Service"
   - Conecta tu repositorio GitHub/GitLab
   - Selecciona el branch `main`
   - **Runtime:** Docker
   - **Root Directory:** `ControlInventario` (si está en subcarpeta)

2. **Configurar Environment Variables:**
   ```bash
   ASPNETCORE_ENVIRONMENT=Production
   ASPNETCORE_URLS=http://+:8080
   ConnectionStrings__ControlInventarioContext=postgresql://user:password@host:port/database
   ```

3. **Configurar Health Check:**
   - Health Check Path: `/health`
   - Auto-deploy: Yes
   - Restart on failure: Yes

### Paso 4: Migración de Base de Datos
Opción A: **Automática (Recomendada)**
```bash
# Agregar al Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

// Antes de app.Run()
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ControlInventarioContext>();
    context.Database.Migrate();
}
```

Opción B: **Manual**
1. Conéctate a la base de datos PostgreSQL en Render
2. Ejecuta tu script SQL de creación de tablas
3. Inserta datos iniciales

### Paso 5: Verificación
1. **Health Check:** `https://tu-app.onrender.com/health`
2. **Aplicación:** `https://tu-app.onrender.com`
3. **Logs:** Revisa los logs en Render Dashboard

## 🔧 Configuración Adicional

### Variables de Entorno Recomendadas:
```bash
# Configuración básica
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080

# Base de datos (reemplaza con tus datos)
ConnectionStrings__ControlInventarioContext=Host=host;Port=5432;Database=database;Username=user;Password=password;SSL Mode=Require;Trust Server Certificate=true

# Seguridad (opcional)
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
```

### Dominio Personalizado (Opcional):
1. Ve a tu Web Service en Render
2. Click "Custom Domains"
3. Agrega tu dominio
4. Configura DNS según instrucciones de Render

## 📋 Checklist Pre-Deploy:

- [ ] Dockerfile creado y probado localmente
- [ ] Health controller implementado
- [ ] Variables de entorno configuradas
- [ ] Base de datos PostgreSQL creada
- [ ] Connection string actualizada
- [ ] Repositorio actualizado con archivos Docker
- [ ] Health check funcionando
- [ ] Logs de aplicación configurados

## 🚨 Troubleshooting:

### Problemas Comunes:
1. **Build Fallido:**
   - Revisa Dockerfile syntax
   - Verifica .dockerignore
   - Check logs en Render

2. **Conexión a BD:**
   - Verifica connection string
   - Confirma que la BD esté activa
   - Check firewall/permisos

3. **Health Check Fallido:**
   - Confirma ruta `/health`
   - Verifica que el controller esté público
   - Check logs para errores

## 💰 Costos Estimados (Render Free Tier):
- **Web Service:** Gratis (hasta 750 horas/mes)
- **PostgreSQL:** Gratis (hasta 90 días, luego ~$7/mes)
- **Dominio:** Gratis (subdominio .onrender.com)

## 🔄 CI/CD Automático:
Render automáticamente:
- Detecta cambios en tu repositorio
- Build del Docker image
- Deploy automático
- Health checks
- Restart en fallos

## 📱 Monitoreo:
- **Dashboard:** Render Dashboard
- **Logs:** Tiempo real y históricos
- **Métricas:** CPU, memoria, requests
- **Alertas:** Configurable por email/slack
