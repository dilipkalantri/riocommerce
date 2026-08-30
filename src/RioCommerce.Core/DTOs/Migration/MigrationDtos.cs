namespace RioCommerce.Core.DTOs.Migration;

/// <summary>Per-entity outcome of an import run.</summary>
public class ImportEntityResult
{
    public string Entity { get; set; } = "";
    public int Read { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<string> Messages { get; set; } = new();   // capped sample of notable rows/errors
}

public class ImportRunResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public bool DryRun { get; set; }
    public ImportEntityResult Products { get; set; } = new() { Entity = "Products" };
    public ImportEntityResult Users { get; set; } = new() { Entity = "Customers/Users" };
    public ImportEntityResult Images { get; set; } = new() { Entity = "Product Images" };
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}

public class ImportRequest
{
    /// <summary>Full SQL Server connection string to the OLD nopCommerce database.</summary>
    public string SourceConnectionString { get; set; } = "";
    /// <summary>When true, read + compute counts but write nothing (preview).</summary>
    public bool DryRun { get; set; } = true;
    public bool ImportProducts { get; set; } = true;
    public bool ImportCustomers { get; set; } = true;

    /// <summary>When true, attach old product images (point-in-place) to imported products.
    /// Requires products to have been imported (uses the legacy product id map).</summary>
    public bool ImportProductImages { get; set; } = false;

    /// <summary>Local folder the NEW server can read that contains the copied nopCommerce
    /// image files (e.g. the old wwwroot/images/thumbs copied into the new app's
    /// wwwroot/images/thumbs, or any reachable folder). Used only to verify each file exists
    /// before writing its public URL. Files are matched by the nopCommerce name
    /// {PictureId:0000000}_{SeoFilename}.{ext}.</summary>
    public string? ImageSourceFolder { get; set; }

    /// <summary>Public URL prefix under which those files are served by the new app's
    /// static-file middleware. Default matches files copied to wwwroot/images/thumbs.</summary>
    public string ImageUrlPrefix { get; set; } = "/images/thumbs/";
}
