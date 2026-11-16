# Test fixtures

### Archives

These tiny 7z archives contain original test text, generated with 7-Zip 26.03.
archive-solid.7z contains empty/ and nested/ directories plus song.txt (Seven zip
payload) and second.txt (Another solid member), with solid compression enabled.
archive-encrypted.7z contains the directories and song.txt, with header encryption;
test password is fixture. Tests require no installed 7-Zip executable.

### VP9 WebM

`abc-vp9-opus.webm` is one second of synthetic video and stereo audio. The
tagging test copies it before editing and checks that both encoded streams
retain their hashes. Fixture generation uses an FFmpeg build with libvpx
and libopus. The runtime under test only needs to read and remux it.

```powershell
ffmpeg -f lavfi -i 'color=c=blue:s=160x120:r=10' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 1 -c:v libvpx-vp9 -c:a libopus -ac 2 -map_metadata -1 abc-vp9-opus.webm
```
