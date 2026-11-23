namespace KaraokeToolkit.Cli;

/// <summary>Prints opt-in shell setup. Never edits a user's profile or environment.</summary>
internal static class CliShellIntegration
{
    private static string[] Words => new[] { "help", "operations", "run", "recipe", "quarantine", "disc-list", "recovery-list", "recover", "completion", "path",
        "--help", "--version", "--input", "--json", "--quiet", "--report", "--options", "--dry-run", "--output", "--overwrite", "--encoder", "--bitrate", "--zip-level", "--zip-algorithm",
        "--recurse", "--no-recurse", "--keep-originals", "--delete-originals", "--cue-text", "--no-cue-text", "--validate-inputs", "--ignore-leading-the", "--pattern",
        "--fill-empty-only", "--preserve-comments", "--preserve-other-tags", "--video-container", "--video-resolution", "--video-smoothing", "--video-frame-rate", "--video-bitrate", "--video-audio-bitrate", "--video-audio-fallback-only",
        "--device", "--tracks", "--disc-name", "--format", "--experimental", "--cdrdao" }
        .Concat(CliArguments.GroupingFlags.Concat(CliArguments.GroupingValues).Select(name => "--" + name))
        .Concat(CliOperationCatalog.Exposed.Select(o => o.Name!)).Distinct().ToArray();

    internal static string Completion(string shell)
    {
        string words = string.Join(' ', Words);
        return shell.ToLowerInvariant() switch
        {
            "powershell" => "Register-ArgumentCompleter -Native -CommandName karaoke-toolkit,karaoke-toolkit.exe -ScriptBlock {\nparam($wordToComplete, $commandAst, $cursorPosition)\n" +
                "('" + words + "' -split ' ') | Where-Object { $_.StartsWith($wordToComplete, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }\n}",
            "bash" => "_karaoke_toolkit_complete() {\n  COMPREPLY=( $(compgen -W '" + words + "' -- \"${COMP_WORDS[COMP_CWORD]}\") )\n}\ncomplete -o default -F _karaoke_toolkit_complete karaoke-toolkit",
            "zsh" => "#compdef karaoke-toolkit\nautoload -Uz compinit\n(( $+functions[compdef] )) || compinit\n_karaoke_toolkit_complete() {\n  local -a choices\n  choices=(" + words + ")\n  _describe 'command or option' choices\n  _files\n}\ncompdef _karaoke_toolkit_complete karaoke-toolkit",
            _ => throw new CliUsageException("Choose powershell, bash or zsh.")
        };
    }

    internal static string PathSetup(string shell)
    {
        string directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        return shell.ToLowerInvariant() switch
        {
            "powershell" => "# Add to $PROFILE for this installation (Velopack updates may change its directory).\n$karaokeCli = '" + directory.Replace("'", "''") + "'\nif (($env:PATH -split [IO.Path]::PathSeparator) -notcontains $karaokeCli) { $env:PATH = $karaokeCli + [IO.Path]::PathSeparator + $env:PATH }",
            "bash" or "zsh" => "# Add to your shell profile for this installation; update this path after moving it.\nexport PATH='" + directory.Replace("'", "'\"'\"'") + "':\"$PATH\"",
            _ => throw new CliUsageException("Choose powershell, bash or zsh.")
        };
    }
}
