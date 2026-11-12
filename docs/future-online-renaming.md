# Future work: online metadata lookup and renaming

Decision (2026-09-26): defer online renaming from the current local-core completion
milestone. Do not replace the legacy provider or repair its parser as part of parity
work. Keep the original DLLs for comparison; the decompiled reference is retained
in Git history. Existing dispatch remains legacy-only;
this note does not disable it or change default RenameBins/AutoRenameZip values.

## Investigation

The original WebRename.Rename tries KJ Pro and then karaokebooks.com, scraping
provider-specific HTML rather than using a documented API:

- http://www.kjpro.com/browse/tracks.php?DiscNo=...
- http://www.karaokebooks.com/md.asp?t=&a=&d=...&dy=n&MaxRecs=30&go=y&p=1

KJ Pro's official homepage explicitly states that it has closed down:
https://www.kjpro.com/

The karaokebooks.com site and original KJ Pro lookup could not be accessed through
the research browser. This is not proof that karaokebooks.com has permanently closed.
A legacy HTML lookup must not be treated as operational merely because its wrapper exists.

## Candidate replacement: iCroons

https://www.icroons.com/
https://www.icroons.com/info_SearchTips.aspx

The public search site is accessible. Its search guidance supports artist, title
and trackID searches. This makes it a candidate for disc/track metadata lookup,
not a confirmed drop-in provider. No supported public API, bulk export, integration
agreement or permission for automated querying has been established in this review.

Before implementing:

1. Confirm a supported access method and usage terms with the provider.
2. Verify legacy disc/track coverage, including manufacturer prefixes and duplicates.
3. Put lookup behind a provider interface; cache responses and test with saved,
   permitted fixtures rather than depending on live services for unit tests.
4. Test missing/ambiguous matches, malformed responses, timeouts and rate limits.
5. Keep metadata lookup separate from rename planning and file execution so paired
   audio/CDG names, collisions and partial failures can be tested independently.
6. Decide how to expose or disable the old automatic rename options in the final GUI.

Continue local conversion tests with RenameBins=false and AutoRenameZip=false where
network independence is needed. These explicit test settings do not redefine original defaults.
