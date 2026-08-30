namespace RioCommerce.Core.DTOs.Franchise;

/// <summary>One franchisee row to import (already mapped from the uploaded sheet).
/// Financial fields (wallet, credit, commissions) are intentionally excluded — set later in admin.</summary>
public class FranchiseImportRow
{
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? ContactPerson { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? AddressLine { get; set; }
    public string? PinCode { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
}

public class FranchiseImportRequest
{
    public List<FranchiseImportRow> Rows { get; set; } = new();
    /// <summary>Preview only — validate + count, write nothing.</summary>
    public bool DryRun { get; set; } = true;
    /// <summary>Run default product-share auto-assignment for each new franchise.</summary>
    public bool AutoAssignProductShares { get; set; } = true;
}

public class FranchiseImportItemResult
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Outcome { get; set; } = "";   // "created" | "updated" | "skipped" | "error"
    public string? Code { get; set; }
    public string? Message { get; set; }
}

public class FranchiseImportResult
{
    public bool Ok { get; set; }
    public bool DryRun { get; set; }
    public int Read { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string? Error { get; set; }
    public List<FranchiseImportItemResult> Items { get; set; } = new();
}

/// <summary>Request to import product↔franchisee share rules from the old ERP SQL Server.</summary>
public class ErpShareImportRequest
{
    /// <summary>SQL Server connection string for the ERP DB (erp_riocommerce_com). Not stored.</summary>
    public string SourceConnectionString { get; set; } = string.Empty;
    /// <summary>Preview only — read + match + validate, write nothing.</summary>
    public bool DryRun { get; set; } = true;
}

public class ErpShareItemResult
{
    public string Sku { get; set; } = "";
    public string FranchiseeEmail { get; set; } = "";
    public string Outcome { get; set; } = "";   // "mapped" | "skipped" | "error"
    public string? Message { get; set; }
}

public class ErpShareImportResult
{
    public bool Ok { get; set; }
    public bool DryRun { get; set; }
    public int Read { get; set; }
    public int Mapped { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string? Error { get; set; }
    public List<ErpShareItemResult> Items { get; set; } = new();
}
