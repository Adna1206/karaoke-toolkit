namespace KaraokeToolkit.Mcp;

/// <summary>Owner-selected process policy; a tool argument alone cannot enable writes.</summary>
public sealed record McpAccessPolicy(bool AllowFileChanges)
{
    public void RequireFileChanges(bool confirmed)
    {
        if (!AllowFileChanges) throw new UnauthorizedAccessException("File changes are disabled. The client owner must launch this process with --allow-writes.");
        if (!confirmed) throw new UnauthorizedAccessException("Review a plan and obtain user authorization, then pass confirmFileChanges=true. Completed changes are not rolled back as a whole batch.");
    }
}
