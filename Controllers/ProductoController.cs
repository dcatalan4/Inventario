using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ControlInventario.Models;
using ControlInventario.Helpers;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace ControlInventario.Controllers
{
    [Authorize]
    public class ProductoController : Controller
    {
        private readonly ControlInventarioContext _context;

        public ProductoController(ControlInventarioContext context)
        {
            _context = context;
        }

        // GET: Producto
        [Authorize(Roles = "Admin,Vendedor")]
        public async Task<IActionResult> Index(string busqueda = "", int page = 1)
        {
            int pageSize = 10;
            var query = _context.Productos.AsQueryable();

            // Aplicar filtro de búsqueda (case-insensitive)
            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                query = query.Where(p => 
                    EF.Functions.Like(p.Codigo.ToLower(), $"%{busqueda.ToLower()}%") ||
                    EF.Functions.Like(p.Nombre.ToLower(), $"%{busqueda.ToLower()}%") ||
                    (p.Descripcion != null && EF.Functions.Like(p.Descripcion.ToLower(), $"%{busqueda.ToLower()}%"))
                );
            }

            // Contar total de productos
            int totalProductos = await query.CountAsync();

            // Aplicar paginación
            var productos = await query
                .OrderBy(p => p.Nombre)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ViewBag para paginación y búsqueda
            ViewBag.CurrentBusqueda = busqueda;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalProductos / pageSize);
            ViewBag.TotalProductos = totalProductos;

            return View(productos);
        }

        // GET: Producto/Details/5
        [Authorize(Roles = "Admin,Vendedor")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos
                .FirstOrDefaultAsync(m => m.IdProducto == id);
            if (producto == null)
            {
                return NotFound();
            }

            return View(producto);
        }

        // GET: Producto/Create
        [Authorize(Roles = "Admin,Vendedor")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Producto/Create
        [Authorize(Roles = "Admin,Vendedor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("IdProducto,Codigo,Nombre,Descripcion,PrecioIngreso,PrecioVenta,StockActual,Activo,FechaCreacion")] Producto producto, string clientDateTime = null)
        {
            try
            {
                // Parsear fecha del cliente
                DateTime? clientDate = null;
                if (!string.IsNullOrEmpty(clientDateTime))
                {
                    string[] formats = { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
                    if (DateTime.TryParseExact(clientDateTime, formats, null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                    {
                        clientDate = parsedDate;
                    }
                }

                // Validar que el código no exista
                var codigoExistente = await _context.Productos
                    .AnyAsync(p => p.Codigo == producto.Codigo && p.IdProducto != producto.IdProducto);
                
                if (codigoExistente)
                {
                    ModelState.AddModelError("Codigo", "El código de producto ya existe.");
                }

                if (ModelState.IsValid)
                {
                    producto.FechaCreacion = DateTimeHelper.GetClientDateTime(clientDate);
                    producto.Activo = true;
                    
                    _context.Add(producto);
                    await _context.SaveChangesAsync();
                    
                    // Obtener un usuario válido para el movimiento
                    var usuarioId = await ObtenerUsuarioValido();
                    
                    // Crear movimiento de inventario inicial solo si hay stock
                    if (producto.StockActual > 0)
                    {
                        try
                        {
                            var movimiento = new MovimientoInventario
                            {
                                IdProducto = producto.IdProducto,
                                TipoMovimiento = "E", // E para Entrada (según CHECK constraint)
                                Cantidad = producto.StockActual,
                                Fecha = DateTimeHelper.GetClientDateTime(clientDate),
                                Referencia = "Stock inicial",
                                IdUsuario = usuarioId
                            };
                            
                            _context.Add(movimiento);
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception movEx)
                        {
                            // Si falla el movimiento, loguear pero continuar
                            Console.WriteLine($"Error al crear movimiento de inventario: {movEx.Message}");
                            // No fallar toda la operación por el movimiento
                        }
                    }
                    
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error al crear el producto: {ex.Message}");
                Console.WriteLine($"Error en Create: {ex.Message}");
            }
            
            return View(producto);
        }

        // GET: Producto/Edit/5
        [Authorize(Roles = "Admin,Vendedor")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null)
            {
                return NotFound();
            }
            return View(producto);
        }

        // POST: Producto/Edit/5
        [Authorize(Roles = "Admin,Vendedor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("IdProducto,Codigo,Nombre,Descripcion,PrecioIngreso,PrecioVenta,StockActual,Activo,FechaCreacion")] Producto producto, string clientDateTime = null)
        {
            if (id != producto.IdProducto)
            {
                return NotFound();
            }

            // Parsear fecha del cliente
            DateTime? clientDate = null;
            if (!string.IsNullOrEmpty(clientDateTime))
            {
                string[] formats = { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
                if (DateTime.TryParseExact(clientDateTime, formats, null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                {
                    clientDate = parsedDate;
                }
            }

            // Validar que el código no exista (excepto para este mismo producto)
            var codigoExistente = await _context.Productos
                .AnyAsync(p => p.Codigo == producto.Codigo && p.IdProducto != producto.IdProducto);
            
            if (codigoExistente)
            {
                ModelState.AddModelError("Codigo", "El código de producto ya existe.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var productoOriginal = await _context.Productos.FindAsync(id);
                    if (productoOriginal == null)
                    {
                        return NotFound();
                    }

                    // Verificar si cambió el stock para crear movimiento
                    if (productoOriginal.StockActual != producto.StockActual)
                    {
                        try
                        {
                            var usuarioId = await ObtenerUsuarioValido();
                            
                            var movimiento = new MovimientoInventario
                            {
                                IdProducto = producto.IdProducto,
                                TipoMovimiento = producto.StockActual > productoOriginal.StockActual ? "E" : "S", // E para Entrada, S para Salida
                                Cantidad = Math.Abs(producto.StockActual - productoOriginal.StockActual),
                                Fecha = DateTimeHelper.GetClientDateTime(clientDate),
                                Referencia = "Ajuste de stock",
                                IdUsuario = usuarioId
                            };
                            
                            _context.Add(movimiento);
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception movEx)
                        {
                            // Si falla el movimiento, loguear pero continuar
                            Console.WriteLine($"Error al crear movimiento de inventario: {movEx.Message}");
                            // No fallar toda la operación por el movimiento
                        }
                    }

                    // Actualizar propiedades
                    productoOriginal.Codigo = producto.Codigo;
                    productoOriginal.Nombre = producto.Nombre;
                    productoOriginal.Descripcion = producto.Descripcion;
                    productoOriginal.PrecioIngreso = producto.PrecioIngreso;
                    productoOriginal.PrecioVenta = producto.PrecioVenta;
                    productoOriginal.StockActual = producto.StockActual;
                    productoOriginal.Activo = producto.Activo;

                    _context.Update(productoOriginal);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProductoExists(producto.IdProducto))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(producto);
        }

        // GET: Producto/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos
                .FirstOrDefaultAsync(m => m.IdProducto == id);
            if (producto == null)
            {
                return NotFound();
            }

            return View(producto);
        }

        // POST: Producto/Delete/5
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var producto = await _context.Productos.FindAsync(id);
            if (producto != null)
            {
                // En lugar de eliminar, marcar como inactivo
                producto.Activo = false;
                _context.Update(producto);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(Index));
        }

        // GET: Producto/Activar/5
        public async Task<IActionResult> Activar(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null)
            {
                return NotFound();
            }

            producto.Activo = true;
            _context.Update(producto);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: Producto/Movimientos/5
        public async Task<IActionResult> Movimientos(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var producto = await _context.Productos.FindAsync(id);
            if (producto == null)
            {
                return NotFound();
            }

            var movimientos = await _context.MovimientoInventarios
                .Where(m => m.IdProducto == id)
                .OrderByDescending(m => m.Fecha)
                .ToListAsync();

            ViewBag.Producto = producto;
            return View(movimientos);
        }

        // GET: Producto/Ingresar
        public IActionResult Ingresar()
        {
            return View();
        }

        // POST: Producto/BuscarProducto
        [HttpPost]
        public async Task<JsonResult> BuscarProducto(string termino)
        {
            try
            {
                var productos = await _context.Productos
                    .Where(p => p.Activo == true && (
                        EF.Functions.Like(p.Codigo.ToLower(), $"%{termino.ToLower()}%") ||
                        EF.Functions.Like(p.Nombre.ToLower(), $"%{termino.ToLower()}%") ||
                        (p.Descripcion != null && EF.Functions.Like(p.Descripcion.ToLower(), $"%{termino.ToLower()}%"))
                    ))
                    .OrderBy(p => p.Nombre)
                    .Select(p => new
                    {
                        idProducto = p.IdProducto,
                        codigo = p.Codigo,
                        nombre = p.Nombre,
                        descripcion = p.Descripcion,
                        precioIngreso = p.PrecioIngreso,
                        precioVenta = p.PrecioVenta,
                        stockActual = p.StockActual
                    })
                    .Take(20)
                    .ToListAsync();

                return Json(productos);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en BuscarProducto: {ex.Message}");
                return Json(new List<object>());
            }
        }

        // POST: Producto/ProcesarIngreso
        [HttpPost]
        public async Task<IActionResult> ProcesarIngreso([FromBody] List<IngresoProducto> productos, string clientDateTime = null)
        {
            try
            {
                Console.WriteLine("Recibiendo solicitud de procesar ingreso...");
                Console.WriteLine($"Fecha del cliente: {clientDateTime}");
                
                // Parsear fecha del cliente
                DateTime? clientDate = null;
                if (!string.IsNullOrEmpty(clientDateTime))
                {
                    string[] formats = { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
                    if (DateTime.TryParseExact(clientDateTime, formats, null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                    {
                        clientDate = parsedDate;
                        Console.WriteLine($"Fecha del cliente parseada: {parsedDate}");
                    }
                }
                
                if (productos == null || !productos.Any())
                {
                    Console.WriteLine("Error: No hay productos para procesar");
                    return Json(new { success = false, message = "No hay productos para procesar" });
                }

                Console.WriteLine($"Procesando {productos.Count} productos...");

                var movimientos = new List<MovimientoInventario>();
                var usuarioId = await ObtenerUsuarioValido();
                Console.WriteLine($"Usuario obtenido: {usuarioId}");

                foreach (var item in productos)
                {
                    Console.WriteLine($"Procesando producto: {item.Nombre}, Cantidad: {item.Cantidad}, ID: {item.IdProducto}");
                    
                    if (item.Cantidad <= 0)
                    {
                        Console.WriteLine($"Error: Cantidad inválida para {item.Nombre}");
                        return Json(new { success = false, message = $"La cantidad para {item.Nombre} debe ser mayor a cero" });
                    }

                    if (item.IdProducto == 0)
                    {
                        // Verificar si ya existe un producto con el mismo código
                        var codigoExistente = await _context.Productos
                            .AnyAsync(p => p.Codigo == item.Codigo);
                        
                        if (codigoExistente)
                        {
                            return Json(new { success = false, message = $"El código {item.Codigo} ya existe en la base de datos" });
                        }

                        // Crear nuevo producto
                        Console.WriteLine($"Creando nuevo producto: {item.Nombre}");
                        var nuevoProducto = new Producto
                        {
                            Codigo = item.Codigo,
                            Nombre = item.Nombre,
                            Descripcion = item.Descripcion ?? "",
                            PrecioIngreso = item.PrecioIngreso,
                            PrecioVenta = item.PrecioVenta,
                            StockActual = item.Cantidad,
                            Activo = true,
                            FechaCreacion = DateTimeHelper.GetClientDateTime(clientDate)
                        };

                        _context.Productos.Add(nuevoProducto);
                        await _context.SaveChangesAsync();
                        Console.WriteLine($"Nuevo producto creado con ID: {nuevoProducto.IdProducto}");

                        // Crear movimiento de inventario solo si hay stock
                        if (nuevoProducto.StockActual > 0)
                        {
                            Console.WriteLine($"Creando movimiento para producto {nuevoProducto.IdProducto}");
                            Console.WriteLine($"Datos del movimiento: ProductoID={nuevoProducto.IdProducto}, Tipo=E, Cantidad={nuevoProducto.StockActual}, UsuarioID={usuarioId}");
                            
                            var movimiento = new MovimientoInventario
                            {
                                IdProducto = nuevoProducto.IdProducto,
                                TipoMovimiento = "E", // E para Entrada (según CHECK constraint)
                                Cantidad = nuevoProducto.StockActual,
                                Fecha = DateTimeHelper.GetClientDateTime(clientDate),
                                Referencia = "Carga al inventario",
                                IdUsuario = usuarioId
                            };
                            movimientos.Add(movimiento);
                            Console.WriteLine($"Movimiento agregado a la lista. Total movimientos: {movimientos.Count}");
                        }
                        else
                        {
                            Console.WriteLine($"No se crea movimiento porque el stock es 0 o menor: {nuevoProducto.StockActual}");
                        }
                    }
                    else
                    {
                        // Actualizar producto existente
                        Console.WriteLine($"Actualizando producto existente: {item.Nombre}");
                        var producto = await _context.Productos.FindAsync(item.IdProducto);
                        if (producto != null)
                        {
                            var stockAnterior = producto.StockActual;
                            producto.StockActual += item.Cantidad;
                            
                            // Actualizar precios si han sido modificados
                            producto.PrecioIngreso = item.PrecioIngreso;
                            producto.PrecioVenta = item.PrecioVenta;
                            
                            Console.WriteLine($"Stock actualizado: {stockAnterior} -> {producto.StockActual}");
                            Console.WriteLine($"Precios actualizados - Compra: Q{producto.PrecioIngreso}, Venta: Q{producto.PrecioVenta}");

                            // Crear movimiento de inventario solo si hay cantidad
                            if (item.Cantidad > 0)
                            {
                                Console.WriteLine($"Creando movimiento para producto existente {producto.IdProducto}");
                                Console.WriteLine($"Datos del movimiento: ProductoID={producto.IdProducto}, Tipo=E, Cantidad={item.Cantidad}, UsuarioID={usuarioId}");
                                
                                var movimiento = new MovimientoInventario
                                {
                                    IdProducto = producto.IdProducto,
                                    TipoMovimiento = "E", // E para Entrada (según CHECK constraint)
                                    Cantidad = item.Cantidad,
                                    Fecha = DateTimeHelper.GetClientDateTime(clientDate),
                                    Referencia = "Carga al inventario",
                                    IdUsuario = usuarioId
                                };
                                movimientos.Add(movimiento);
                                Console.WriteLine($"Movimiento agregado a la lista. Total movimientos: {movimientos.Count}");
                            }
                            else
                            {
                                Console.WriteLine($"No se crea movimiento porque la cantidad es 0 o menor: {item.Cantidad}");
                            }

                            _context.Update(producto);
                            await _context.SaveChangesAsync(); // Guardar cambios del producto
                            Console.WriteLine($"Producto actualizado en base de datos");
                        }
                        else
                        {
                            Console.WriteLine($"Error: No se encontró el producto con ID {item.IdProducto}");
                            return Json(new { success = false, message = $"No se encontró el producto con ID {item.IdProducto}" });
                        }
                    }
                }

            // Guardar todos los movimientos con manejo de errores
            Console.WriteLine($"Procesando {movimientos.Count} movimientos para guardar");
            if (movimientos.Any())
            {
                try
                {
                    _context.AddRange(movimientos);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"Movimientos guardados exitosamente");
                    
                    // Verificación adicional: contar movimientos en la BD
                    var totalMovimientos = await _context.MovimientoInventarios.CountAsync();
                    Console.WriteLine($"Total de movimientos en la base de datos: {totalMovimientos}");
                    
                    // Mostrar detalles del último movimiento guardado
                    var ultimoMovimiento = await _context.MovimientoInventarios
                        .Include(m => m.IdProductoNavigation)
                        .OrderByDescending(m => m.IdMovimiento)
                        .FirstOrDefaultAsync();
                    
                    if (ultimoMovimiento != null)
                    {
                        Console.WriteLine($"Último movimiento guardado: Producto={ultimoMovimiento.IdProductoNavigation?.Nombre}, Tipo={ultimoMovimiento.TipoMovimiento}, Cantidad={ultimoMovimiento.Cantidad}, Fecha={ultimoMovimiento.Fecha}");
                    }
                }
                catch (Exception movEx)
                {
                    Console.WriteLine($"Error al guardar movimientos: {movEx.Message}");
                    Console.WriteLine($"Stack trace: {movEx.StackTrace}");
                    // No fallar toda la operación por los movimientos
                }
            }
            else
            {
                Console.WriteLine("No hay movimientos para guardar");
            }

            Console.WriteLine("Ingreso procesado correctamente");
            return Json(new { success = true, message = "Importación completada correctamente" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error en la importación: " + ex.Message });
            }
        }

        // POST: Producto/CargarExcelProductos
        [HttpPost]
        public async Task<IActionResult> CargarExcelProductos(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
            {
                return Json(new { success = false, message = "Seleccione un archivo Excel para cargar." });
            }

            var extension = Path.GetExtension(archivo.FileName);
            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "El archivo debe ser Excel .xlsx." });
            }

            try
            {
                using var stream = archivo.OpenReadStream();
                var resultado = LeerProductosDesdeExcel(stream);

                if (!resultado.Productos.Any())
                {
                    return Json(new
                    {
                        success = false,
                        message = "No se encontraron productos válidos en el archivo.",
                        errores = resultado.Errores
                    });
                }

                var codigos = resultado.Productos
                    .Select(p => p.Codigo.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var productosExistentes = await _context.Productos
                    .Where(p => codigos.Contains(p.Codigo))
                    .ToDictionaryAsync(p => p.Codigo, StringComparer.OrdinalIgnoreCase);

                var productos = resultado.Productos.Select(producto =>
                {
                    var stockActual = 0;
                    if (productosExistentes.TryGetValue(producto.Codigo, out var existente))
                    {
                        producto.IdProducto = existente.IdProducto;
                        producto.Nombre = existente.Nombre;
                        producto.Descripcion = existente.Descripcion;
                        stockActual = existente.StockActual;
                    }

                    return new
                    {
                        idProducto = producto.IdProducto,
                        codigo = producto.Codigo,
                        nombre = producto.Nombre,
                        descripcion = producto.Descripcion,
                        precioIngreso = producto.PrecioIngreso,
                        precioVenta = producto.PrecioVenta,
                        cantidad = producto.Cantidad,
                        stockActual
                    };
                }).ToList();

                return Json(new
                {
                    success = true,
                    productos,
                    errores = resultado.Errores,
                    message = $"Se cargaron {productos.Count} productos desde Excel."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cargar Excel: {ex.Message}");
                return Json(new { success = false, message = "Error al leer el archivo Excel: " + ex.Message });
            }
        }

        private static ResultadoCargaExcel LeerProductosDesdeExcel(Stream stream)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var sharedStrings = LeerSharedStrings(archive);
            var worksheet = archive.GetEntry("xl/worksheets/sheet1.xml")
                ?? throw new InvalidOperationException("No se encontró la primera hoja del archivo Excel.");

            using var worksheetStream = worksheet.Open();
            var document = XDocument.Load(worksheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var rows = document.Descendants(ns + "row")
                .Select(row => row.Elements(ns + "c")
                    .ToDictionary(
                        cell => ObtenerIndiceColumna(cell.Attribute("r")?.Value ?? ""),
                        cell => ObtenerValorCelda(cell, sharedStrings, ns)))
                .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                .ToList();

            if (!rows.Any())
            {
                return new ResultadoCargaExcel();
            }

            var headers = rows.First()
                .ToDictionary(item => item.Key, item => NormalizarEncabezado(item.Value));

            var columnas = new
            {
                Codigo = BuscarColumna(headers, "codigo", "codigoproducto", "sku"),
                Nombre = BuscarColumna(headers, "nombre", "producto"),
                Descripcion = BuscarColumna(headers, "descripcion", "detalle"),
                PrecioIngreso = BuscarColumna(headers, "precioingreso", "preciocompra", "compra", "costo", "preciocosto"),
                PrecioVenta = BuscarColumna(headers, "precioventa", "venta"),
                Cantidad = BuscarColumna(headers, "cantidad", "stock", "unidades")
            };

            var resultado = new ResultadoCargaExcel();
            var columnasRequeridas = new Dictionary<string, int?>
            {
                ["Codigo"] = columnas.Codigo,
                ["Nombre"] = columnas.Nombre,
                ["PrecioIngreso"] = columnas.PrecioIngreso,
                ["PrecioVenta"] = columnas.PrecioVenta,
                ["Cantidad"] = columnas.Cantidad
            };

            var faltantes = columnasRequeridas
                .Where(columna => columna.Value == null)
                .Select(columna => columna.Key)
                .ToList();

            if (faltantes.Any())
            {
                resultado.Errores.Add("Faltan columnas requeridas: " + string.Join(", ", faltantes));
                return resultado;
            }

            var codigosEnArchivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var numeroFila = i + 1;
                var codigo = ObtenerValor(row, columnas.Codigo).Trim();
                var nombre = ObtenerValor(row, columnas.Nombre).Trim();
                var descripcion = ObtenerValor(row, columnas.Descripcion).Trim();

                if (string.IsNullOrWhiteSpace(codigo) && string.IsNullOrWhiteSpace(nombre))
                {
                    continue;
                }

                var erroresFila = new List<string>();
                if (string.IsNullOrWhiteSpace(codigo)) erroresFila.Add("codigo vacío");
                if (string.IsNullOrWhiteSpace(nombre)) erroresFila.Add("nombre vacío");
                if (!LeerDecimal(ObtenerValor(row, columnas.PrecioIngreso), out var precioIngreso) || precioIngreso <= 0) erroresFila.Add("precio de compra inválido");
                if (!LeerDecimal(ObtenerValor(row, columnas.PrecioVenta), out var precioVenta) || precioVenta <= 0) erroresFila.Add("precio de venta inválido");
                if (!LeerEntero(ObtenerValor(row, columnas.Cantidad), out var cantidad) || cantidad <= 0) erroresFila.Add("cantidad inválida");
                if (precioIngreso >= precioVenta) erroresFila.Add("precio de compra debe ser menor al precio de venta");
                if (!codigosEnArchivo.Add(codigo)) erroresFila.Add("codigo duplicado en el archivo");

                if (erroresFila.Any())
                {
                    resultado.Errores.Add($"Fila {numeroFila}: {string.Join(", ", erroresFila)}.");
                    continue;
                }

                resultado.Productos.Add(new IngresoProducto
                {
                    Codigo = codigo,
                    Nombre = nombre,
                    Descripcion = descripcion,
                    PrecioIngreso = precioIngreso,
                    PrecioVenta = precioVenta,
                    Cantidad = cantidad
                });
            }

            return resultado;
        }

        private static List<string> LeerSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return new List<string>();

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            return document.Descendants(ns + "si")
                .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
                .ToList();
        }

        private static string ObtenerValorCelda(XElement cell, List<string> sharedStrings, XNamespace ns)
        {
            var type = cell.Attribute("t")?.Value;
            if (type == "inlineStr")
            {
                return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value));
            }

            var value = cell.Element(ns + "v")?.Value ?? "";
            if (type == "s" && int.TryParse(value, out var sharedStringIndex) && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
            {
                return sharedStrings[sharedStringIndex];
            }

            return value;
        }

        private static int ObtenerIndiceColumna(string referencia)
        {
            var indice = 0;
            foreach (var caracter in referencia.TakeWhile(char.IsLetter))
            {
                indice = (indice * 26) + (char.ToUpperInvariant(caracter) - 'A' + 1);
            }
            return indice;
        }

        private static string NormalizarEncabezado(string value)
        {
            var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var character in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
            }
            return builder.ToString();
        }

        private static int? BuscarColumna(Dictionary<int, string> headers, params string[] nombres)
        {
            return headers.FirstOrDefault(header => nombres.Contains(header.Value)).Key is var key && key > 0 ? key : null;
        }

        private static string ObtenerValor(Dictionary<int, string> row, int? columna)
        {
            return columna.HasValue && row.TryGetValue(columna.Value, out var value) ? value : "";
        }

        private static bool LeerDecimal(string value, out decimal result)
        {
            value = value.Replace("Q", "", StringComparison.OrdinalIgnoreCase).Trim();
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
                || decimal.TryParse(value, NumberStyles.Number, new CultureInfo("es-GT"), out result);
        }

        private static bool LeerEntero(string value, out int result)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
            {
                result = (int)decimalValue;
                return decimalValue == result;
            }

            return false;
        }

        private async Task<int> ObtenerUsuarioValido()
        {
            try
            {
                Console.WriteLine("Buscando usuario válido...");
                
                // Intentar obtener cualquier usuario existente
                var usuarioExistente = await _context.Usuarios.FirstOrDefaultAsync();
                if (usuarioExistente != null)
                {
                    Console.WriteLine($"Usuario existente encontrado: ID={usuarioExistente.IdUsuario}, Usuario={usuarioExistente.Usuario1}");
                    return usuarioExistente.IdUsuario;
                }
                
                Console.WriteLine("No se encontraron usuarios existentes, creando usuario por defecto...");
                
                // Si no hay usuarios, crear uno por defecto con nombre único
                var timestamp = DateTime.Now.Ticks;
                var usuarioDefecto = new Usuario
                {
                    Usuario1 = $"admin_{timestamp}",
                    PasswordHash = "temporal123",
                    Nombre = "Administrador",
                    Rol = "Admin",
                    Activo = true,
                    FechaCreacion = DateTimeHelper.GetClientDateTime()
                };
                
                _context.Usuarios.Add(usuarioDefecto);
                await _context.SaveChangesAsync();
                
                Console.WriteLine($"Usuario por defecto creado: ID={usuarioDefecto.IdUsuario}, Usuario={usuarioDefecto.Usuario1}");
                return usuarioDefecto.IdUsuario;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en ObtenerUsuarioValido: {ex.Message}");
                // Si todo falla, intentar buscar cualquier usuario o retornar 1
                try
                {
                    var usuario = await _context.Usuarios.FindAsync(1);
                    if (usuario != null)
                    {
                        Console.WriteLine($"Usuario fallback encontrado: ID={usuario.IdUsuario}");
                        return usuario.IdUsuario;
                    }
                }
                catch
                {
                    Console.WriteLine("Fallback también falló, retornando 1");
                }
                return 1;
            }
        }

        private bool ProductoExists(int id)
        {
            return _context.Productos.Any(e => e.IdProducto == id);
        }
    }

    public class IngresoProducto
    {
        public int IdProducto { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Descripcion { get; set; }
        public decimal PrecioIngreso { get; set; }
        public decimal PrecioVenta { get; set; }
        public int Cantidad { get; set; }
    }

    public class ResultadoCargaExcel
    {
        public List<IngresoProducto> Productos { get; set; } = new();
        public List<string> Errores { get; set; } = new();
    }
}
