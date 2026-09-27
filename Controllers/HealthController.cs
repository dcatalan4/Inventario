using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ControlInventario.Controllers
{
    [ApiController]
    [Route("health")]
    public class HealthController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public HealthController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                service = "FarmaClinic API"
            });
        }

        [HttpGet("db-config")]
        public IActionResult GetDatabaseConfig()
        {
            var connectionString = _configuration.GetConnectionString("ControlFarmaclinicContext");
            if (string.IsNullOrWhiteSpace(connectionString))
                return Ok(new { configured = false });

            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return Ok(new
            {
                configured = true,
                host = builder.Host,
                database = builder.Database,
                username = builder.Username
            });
        }
    }
}
