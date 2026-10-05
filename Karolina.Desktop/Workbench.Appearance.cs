using Karolina.Core.Appearance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Karolina.Desktop;

public sealed partial class Workbench
{
    private void MapAppearanceRoutes()
    {
        var server = app ?? throw new InvalidOperationException("工作台服务尚未创建");
        string webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
        var appearance = new AppearanceStore(project, Path.Combine(webRoot, "themes"));
        var layout = new PanelLayoutStore(project);
        var mime = new FileExtensionContentTypeProvider();
        mime.Mappings[".frag"] = "text/plain";
        mime.Mappings[".glsl"] = "text/plain";
        server.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRoot), ContentTypeProvider = mime });
        server.MapGet("/api/appearance", () => new { preferences = appearance.Read(), themes = appearance.List() });
        server.MapPost("/api/appearance", (AppearancePreferences value) => { appearance.Save(value); return new { saved = true }; });
        server.MapGet("/api/layout", () => layout.Read());
        server.MapPost("/api/layout", (PanelLayoutPreferences value) => { layout.Save(value); return new { saved = true }; });
        server.MapGet("/theme-assets/{id}/{**asset}", (string id, string asset, HttpContext context) =>
        {
            string file = appearance.AssetPath(id, asset);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.File(file, mime.TryGetContentType(file, out string? contentType) ? contentType : "text/plain");
        });
        server.MapPost("/api/appearance/import", async (HttpRequest request) =>
        {
            // Raw ZIP keeps the upload independent of multipart model binding and its antiforgery assumptions.
            if (request.ContentType != "application/zip") throw new ArgumentException("请选择 ZIP 主题包");
            const int limit = 64 * 1024 * 1024;
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
            {
                if (memory.Length + count > limit) throw new ArgumentException("主题包上传体积不能超过 64 MB");
                memory.Write(buffer, 0, count);
            }
            memory.Position = 0;
            return appearance.Import(memory, request.Query["replace"] == "true");
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(64L * 1024 * 1024));
        server.MapPost("/api/appearance/export", (AppearancePreferences value) => Results.File(appearance.Export(value.ThemeId, value), "application/zip", value.ThemeId + "-theme.zip"));
    }
}
