# Future work: online metadata lookup and renaming

Online metadata lookup and filename renaming are deferred. The current application
does not provide a working online provider. Local filename cleanup and filename-based
metadata tagging are separate, implemented features.

## Provider research

The 2026-09-26 investigation considered KJ Pro, karaokebooks.com and iCroons.
It recorded a closure notice on KJ Pro's homepage and could not access the
karaokebooks.com search site. These are dated observations, not a current service
availability check. No supported provider integration was established.

- [KJ Pro](https://www.kjpro.com/)
- [Karaoke Books](http://www.karaokebooks.com/)

## Candidate provider: iCroons

[iCroons](https://www.icroons.com/) and its
[search guidance](https://www.icroons.com/info_SearchTips.aspx) were considered as
possible sources for artist, title and track-ID lookup. Recheck availability,
supported APIs, coverage and usage terms before choosing a provider. A public search
page alone is not an integration API or permission for automated querying.

Before implementing:

1. Confirm a supported access method and usage terms with the provider.
2. Verify disc/track coverage, including manufacturer prefixes and duplicates.
3. Put lookup behind a provider interface; cache responses and test with saved,
   permitted fixtures rather than depending on live services for unit tests.
4. Test missing/ambiguous matches, malformed responses, timeouts and rate limits.
5. Keep metadata lookup separate from rename planning and file execution so paired
   audio/CDG names, collisions and partial failures can be tested independently.
6. Design explicit preview and confirmation of proposed names before changing files.

Persisted `RenameBins` and `AutoRenameZip` keys are retained for settings compatibility;
their presence does not make online renaming operational. Unit tests should use
permitted local fixtures rather than require live provider access.
