# FlatOut2MusicManager

What it does:

* Reads your music in any format
* Converts it and adds to FlatOut 2 race playlist

### Features

* Supported formats: ".mp3", ".ogg", ".wav", ".wma", ".aac", ".ac3", ".flac"
* Reads ID3 tags and tries to embed `Artist - Track` into the game
* Creates a backup of main game archive `fo2a.bfs.bak`

### How to use

0. Have FlatOut 2 (tested for Steam version)
1. Have 6+ GB of free space
2. Place your music in some directory, eg `C:\Music`. Subdirectories are supported too
3. Install dependencies (read below)
4. Run `FlatOut2MusicManager "C:\Program Files (x86)\Steam\steamapps\common\FlatOut2" "C:\Music"`

# Dependencies

## FFMpeg

Multimedia swiss army knife. Used to convert filse into .OGG format.

[Get it here](https://ffmpeg.org/download.html#build-windows)

It has to be installed globally (added in %PATH%)!

## bfs2pack

Tool to work with .bfs archives

[Get it here](https://sourceforge.net/projects/bfs2pack/files/bfs2pack/bfs2pack1.2/bfs2pack1.2-bin.zip/download)

 2 files have to be placed inside game directory:
 
 * bfs2pack_con.exe
 * zlib1.dll
 
 ![bfs2pack](/img/bfs2pack.png)
 
 # How to use
 
 **Arguments:** <FlatOut2 dir> <Music dir>
  
 * First is where game is installed (should have `fo2a.bfs` archive)
 * Second is where you placed your music
  
Example: 

> `FlatOut2MusicManager "C:\Program Files (x86)\Steam\steamapps\common\FlatOut2" "C:\Music"`

---

Release created with this command: `dotnet publish --self-contained -r win-x64`
