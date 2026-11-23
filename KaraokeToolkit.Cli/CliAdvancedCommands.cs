using System.Text.Json;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Cli;

/// <summary>Workflow and hardware commands adapt Core services; they do not implement media processing.</summary>
internal sealed class CliAdvancedCommands(OperationExecutionService service, IDiscReader? discReader, Func<SharedSettingsSnapshot>? loadProfile)
{
    internal async Task<int?> TryRunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token)
    {
        if (args.Length == 0) return null;
        string command = args[0].ToLowerInvariant();
        if (command == "help")
        {
            if (args.Length > 2) throw new CliUsageException("help takes at most one command or operation.");
            await output.WriteLineAsync(CliHelp.For(args.ElementAtOrDefault(1))); return 0;
        }
        if (command is not ("recipe" or "disc-list" or "quarantine" or "completion" or "path") &&
            !CliOperationCatalog.Get(OperationId.DiscRip).MatchesName(command)) return null;
        if (args.TakeWhile(a => a != "--").Any(a => a is "--help" or "-h"))
        { await output.WriteLineAsync(CliHelp.For(command)); return 0; }
        if (command is "completion" or "path")
        {
            if (args.Length != 2) throw new CliUsageException(CliHelp.For(command));
            await output.WriteLineAsync(command == "completion" ? CliShellIntegration.Completion(args[1]) : CliShellIntegration.PathSetup(args[1])); return 0;
        }
        if (command == "recipe") return await RecipeAsync(args[1..], output, error, token);
        if (command == "quarantine")
        {
            CheckFlags(args[1..], ["json", "quiet", "report", "dry-run", "input", "recurse", "no-recurse"]);
            var parsed = CliArguments.Parse([OperationCommandNames.Get(OperationId.ValidateKaraoke), .. args[1..]], loadProfile);
            var result = await service.QuarantineAsync(parsed.Inputs, parsed.Request!.Options.RecurseFolders,
                parsed.Quiet ? null : new CliApplication.ConsoleProgress(error), token, parsed.DryRun);
            return await CliApplication.WriteResultAsync(parsed, result, output, error);
        }
        var rest = args[1..].ToList();
        string? executable = Take(rest, "cdrdao");
        var reader = discReader ?? new NativeDiscReader(executable);
        if (command == "disc-list")
        {
            if (rest.Any(a => a != "--json")) throw new CliUsageException(CliHelp.For(command));
            var discs = await reader.ScanAsync(token);
            if (rest.Contains("--json")) await output.WriteLineAsync(JsonSerializer.Serialize(discs, CliApplication.JsonOptions));
            else foreach (var disc in discs) await output.WriteLineAsync($"{disc.Device ?? disc.Drive.ToString()}: {disc} ({disc.Tracks.Length} tracks)");
            return 0;
        }
        string device = Take(rest, "device") ?? throw new CliUsageException("--device is required; use disc-list.");
        string destination = Take(rest, "output") ?? throw new CliUsageException("--output is required.");
        string name = Take(rest, "disc-name") ?? throw new CliUsageException("--disc-name is required.");
        string formatText = Take(rest, "format") ?? "Bin";
        if (!Enum.TryParse<RipFormat>(formatText, true, out var format) || !Enum.IsDefined(format) || int.TryParse(formatText, out _)) throw new CliUsageException("--format must be Bin, AudioG or Zip.");
        string tracksText = Take(rest, "tracks") ?? "all";
        if (!rest.Remove("--experimental")) throw new CliUsageException("Pass --experimental to acknowledge experimental physical CD+G capture.");
        var parsedRip = CliArguments.Parse(["run", "bin-to-audiog", ".", .. rest], loadProfile);
        if (parsedRip.Inputs.Length != 1 || parsedRip.Inputs[0] != ".") throw new CliUsageException("rip-cdg requires --device and --output; remove positional input paths.");
        var options = parsedRip.Request!.Options with { ExperimentalDiscRipping = true, CdrdaoExecutablePath = executable ?? parsedRip.Request.Options.CdrdaoExecutablePath };
        reader = discReader ?? new NativeDiscReader(options.CdrdaoExecutablePath);
        new ConversionRequest(OperationId.BinToZip, destination, options).Validate();
        if (!Directory.Exists(destination)) throw new CliUsageException("Choose an existing output folder.");
        var available = await reader.ScanAsync(token);
        var selected = available.SingleOrDefault(d => d.Device == device) ?? throw new CliUsageException("Device was not found; use disc-list and its exact device address.");
        int[] tracks = Tracks(tracksText, selected);
        var request = new DiscRipRequest(selected, tracks, Path.GetFullPath(destination), name, format, options);
        DiscRipService.ValidateRequest(request, parsedRip.Request.OverwritePolicy);
        ExecutionResult execution;
        if (parsedRip.DryRun)
        {
            if (!selected.SupportsCdg || tracks.Any(n => !selected.Tracks.Any(t => t.Number == n))) throw new CliUsageException("Selected disc/tracks do not support CD+G ripping.");
            var entries = tracks.Select(n =>
            {
                var targets = DiscRipService.GetOutputPaths(request, n);
                bool blocked = targets.Any(p => Directory.Exists(p) || File.Exists(p)) && parsedRip.Request.OverwritePolicy == OverwritePolicy.Fail;
                return new ReportEntry($"{device} track {n}", blocked ? "Blocked" : "Planned",
                    $"Rip to {format}; physical reading and verification occur during execution. Existing outputs: {parsedRip.Request.OverwritePolicy}.", targets);
            }).ToArray();
            execution = new(new("DiscRip", DateTimeOffset.Now, false, entries) { ReportType = "Dry Run Report" }, tracks.Length, entries.Any(e => e.Status == "Blocked"));
        }
        else
        {
            var ripped = await new DiscRipService(reader).RunAsync(request, parsedRip.Request.OverwritePolicy,
                parsedRip.Quiet ? null : new CliApplication.ConsoleProgress(error), token);
            execution = new(CompletionReport.FromConversion("DiscRip", ripped), ripped.Found, ripped.Failed > 0);
        }
        return await CliApplication.WriteResultAsync(parsedRip, execution, output, error);
    }

    private async Task<int> RecipeAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token)
    {
        if (args.Length < 2) throw new CliUsageException(CliHelp.For("recipe"));
        string action = args[0].ToLowerInvariant(); string file = args[1];
        if (action == "list")
        {
            if (args.Skip(2).Any(a => a != "--json")) throw new CliUsageException(CliHelp.For("recipe"));
            var recipes = new RecipeStore(file).Load();
            if (args.Contains("--json")) await output.WriteLineAsync(JsonSerializer.Serialize(recipes, CliApplication.JsonOptions));
            else foreach (var recipe in recipes) await output.WriteLineAsync($"{recipe.Id}: {recipe.Name} ({recipe.Steps.Count} steps)");
            return 0;
        }
        if (action is not ("show" or "validate" or "run")) throw new CliUsageException(CliHelp.For("recipe"));
        var definition = RecipeJson.Deserialize(await File.ReadAllTextAsync(file, token));
        if (action != "show") OperationExecutionService.RequireUnattendedRecipe(definition);
        if (action is "show" or "validate")
        {
            if (args.Skip(2).Any(a => a != "--json")) throw new CliUsageException(CliHelp.For("recipe"));
            await output.WriteLineAsync(action == "show" || args.Contains("--json") ? RecipeJson.Serialize(definition) : $"Valid unattended recipe: {definition.Name} ({definition.Steps.Count} steps)"); return 0;
        }
        CheckFlags(args[2..], ["json", "quiet", "report", "dry-run", "input"]);
        var parsed = CliArguments.Parse(["run", "audiog-to-zip", .. args[2..]]);
        var progress = parsed.Quiet ? null : new CliApplication.ConsoleProgress(error);
        ExecutionResult result;
        if (parsed.DryRun)
        {
            var inputs = RecipeOperations.NormalizeInputs(definition, parsed.Inputs);
            var first = definition.Steps[0];
            var plan = await new OperationPlanner().PlanAsync(new(OperationCatalog.Get(first.OperationId).Id, inputs)
            { Options = first.Options, OverwritePolicy = first.OverwritePolicy, IgnoreLeadingThe = first.IgnoreLeadingThe, TaggingPattern = first.TaggingPattern, Tagging = first.Tagging }, token);
            result = new(plan.Report with { Operation = definition.Name, Entries = plan.Report.Entries.Concat(definition.Steps.Skip(1).Select((s, i) =>
                new ReportEntry($"Step {i + 2}", "Deferred", $"{s.OperationId}: scope depends on preceding outputs; saved settings apply. No step was executed.", []))).ToArray() }, plan.Found, plan.HasIssues);
        }
        else result = await service.ExecuteRecipeAsync(definition, parsed.Inputs, progress, token);
        return await CliApplication.WriteResultAsync(parsed, result, output, error);
    }

    private static string? Take(List<string> args, string name)
    {
        int index = args.FindIndex(a => a == "--" + name || a.StartsWith("--" + name + "=", StringComparison.Ordinal));
        if (index < 0) return null;
        string argument = args[index]; args.RemoveAt(index);
        string value;
        if (argument.Contains('=')) value = argument[(argument.IndexOf('=') + 1)..];
        else
        {
            if (index >= args.Count || args[index].StartsWith("--")) throw new CliUsageException("Missing --" + name + " value.");
            value = args[index]; args.RemoveAt(index);
        }
        if (string.IsNullOrWhiteSpace(value) || args.Any(a => a == "--" + name || a.StartsWith("--" + name + "=", StringComparison.Ordinal))) throw new CliUsageException("Empty or repeated --" + name + ".");
        return value;
    }

    private static int[] Tracks(string text, OpticalDisc disc)
    {
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase)) return disc.Tracks.Select(t => t.Number).ToArray();
        var result = new List<int>();
        foreach (string part in text.Split(','))
        {
            var range = part.Split('-');
            if (range.Length > 2 || !int.TryParse(range[0], out int first) || first is < 1 or > 99) throw new CliUsageException("Invalid --tracks selection.");
            int last = first;
            if (range.Length == 2 && (!int.TryParse(range[1], out last) || last < first || last > 99)) throw new CliUsageException("Invalid track range.");
            result.AddRange(Enumerable.Range(first, last - first + 1));
        }
        if (result.Distinct().Count() != result.Count || result.Count == 0) throw new CliUsageException("Track selections must be nonempty and unique.");
        return result.ToArray();
    }

    private static void CheckFlags(string[] args, string[] allowed)
    {
        foreach (string argument in args.TakeWhile(a => a != "--").Where(a => a.StartsWith("--")))
            if (!allowed.Contains(argument[2..].Split('=')[0])) throw new CliUsageException("Unsupported workflow option: " + argument);
    }
}
