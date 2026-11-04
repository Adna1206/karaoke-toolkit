using KaraokeToolkit.Core.Execution;

namespace KaraokeToolkit.Cli;

internal static class CliHelp
{
    internal static string For(string? topic)
    {
        if (topic is null or "run") return CliApplication.Help;
        if (topic.Equals("disc-rip", StringComparison.OrdinalIgnoreCase) || topic.Equals("DiscRip", StringComparison.OrdinalIgnoreCase))
            return "Usage: karaoke-toolkit disc-rip --device DEVICE --output FOLDER --disc-name NAME --experimental [--tracks all|1,2,4-6] [--format Bin|AudioG|Zip] [--cdrdao EXECUTABLE] [conversion options] [--dry-run]\nPhysical CD+G capture remains experimental. Uses the existing Core ripper and selected encoding settings; no prompts or elevation.";
        try
        {
            var operation = CliOperationCatalog.Resolve(topic);
            var description = OperationCatalog.Get(operation.OperationId);
            string specific = operation.OperationId switch
            {
                OperationId.TagKaraoke or OperationId.PreviewTags => "Tagging: --pattern, --fill-empty-only, --preserve-comments, --preserve-other-tags. Use tag-preview before applying metadata.",
                OperationId.KaraokeToVideo => "Video: --video-container, --video-resolution, --video-smoothing, --video-frame-rate, --video-bitrate, --video-audio-bitrate, --video-audio-fallback-only.",
                OperationId.OrganizeFiles => "Folder tools: --ignore-leading-the. This operation always recurses and moves files.",
                OperationId.OrganizeFolders => "Groups immediate child folders. This operation moves directories; --no-recurse does not change its scope.",
                OperationId.FlattenOneLayer or OperationId.FlattenAllLayers => "Moves entries into selected roots. Depth follows the operation, not --no-recurse.",
                OperationId.RestoreQuarantined => "Inputs must be Damaged batch manifest.json files. Existing originals are protected.",
                OperationId.QuarantineInvalid => "Validate first, then move only eligible failures to Damaged with restoration manifests. --dry-run reports candidates without moving them.",
                OperationId.ValidateKaraoke => "Read-only structural, pairing and archive checks. Use quarantine to explicitly move eligible failures.",
                _ => "Conversion: --options, --encoder, --bitrate, --zip-algorithm, --zip-level, --recurse, --no-recurse, --cue-text, --no-cue-text, --validate-inputs. Only applicable settings are used."
            };
            return $"Karaoke Toolkit CLI — {operation.Name}\n{description.Description}\nCore ID: {description.Identifier}\nUsage: karaoke-toolkit run {operation.Name} PATH... [options]\n" +
                (description.RequiresFolders ? "Inputs: folders only.\n" : "Inputs: supported files or folders.\n") + specific +
                "\nCommon: --input PATH (repeatable), --dry-run, --json, --quiet, --report NEW.json|NEW.txt.\n" +
                "Defaults: keep originals, overwrite fail; --overwrite fail|skip|replace.\nMedia conversions support --output EXISTING_FOLDER with originals kept.\n";
        }
        catch (CliUsageException)
        {
            return topic switch
            {
                "recipe" => "Usage: karaoke-toolkit recipe list FOLDER | show FILE | validate FILE | run FILE PATH... [--dry-run] [--json] [--quiet] [--report NEW.json|NEW.txt]\nRecipe steps retain their saved settings and must use fail/skip/replace, never Ask. recover JOURNAL also resumes recipes.",
                "disc-list" => "Usage: karaoke-toolkit disc-list [--cdrdao EXECUTABLE] [--json]\nScan for cdrdao-compatible discs; requires device access. Does not request elevation.",
                "disc-rip" => "Usage: karaoke-toolkit disc-rip --device DEVICE --output FOLDER --disc-name NAME --experimental [--tracks all|1,2,4-6] [--format Bin|AudioG|Zip] [--cdrdao EXECUTABLE] [conversion options] [--dry-run]\nPhysical CD+G capture remains experimental. Uses the existing Core ripper and selected encoding settings.",
                "quarantine" => "Usage: karaoke-toolkit quarantine PATH... [--no-recurse] [--dry-run] [--json] [--quiet] [--report NEW.json|NEW.txt]\nValidate inputs and move eligible failures into Damaged with restoration manifests; never moves valid files.",
                "completion" => "Usage: karaoke-toolkit completion powershell|bash|zsh\nPrint shell completion setup; evaluate it in your own shell or save to a profile.",
                "path" => "Usage: karaoke-toolkit path powershell|bash|zsh\nPrint optional PATH setup for this installation. Does not modify profiles or environment itself.",
                "recover" or "recovery-list" or "operations" => CliApplication.Help,
                _ => throw new CliUsageException("Unknown help topic: " + topic)
            };
        }
    }
}
