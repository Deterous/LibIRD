## How to use the LibIRD library

LibIRD was originally made for creating reproducible, redump-style IRDs when dumping PS3 discs with [MPF](https://github.com/SabreTools/MPF). If you wish to integrate LibIRD into your own application, the following are some examples.

### Reproducible, redump-style IRDs

The standard way of generating a reproducible, redump-style IRD is with a redump ISO file and a redump key file (e.g. http://redump.org/disc/28721/key/):
```cs
byte[] discKey = File.ReadAllBytes("./game.key");
IRD ird = new ReIRD("./game.iso", discKey);
```
Alternatively, the disc key can be extracted from a [GetKey](https://archive.org/download/GetKeyR2GameOS.7z/GetKey-r2-GameOS.7z) log file obtained from a PS3 (or when dumping using [ManaGunZ](https://github.com/Zarh/ManaGunZ/))
```cs
IRD ird = new ReIRD("./game.iso", "./game.getkey.log");
```

### Custom IRDs

Functionality is also provided for creating IRDs with a custom disc key, disc ID, and PIC:
```cs
byte[] discKey = new byte[16];
byte[] discID = new byte[16];
byte[] PIC = new byte[115];
// Set vars to desired values
IRD ird = new IRD("./game.iso", discKey, discID, discPIC);
```
As before, a GetKey log file can also be used with an ISO to create a custom IRD, with the disc key, disc ID, and PIC all being extracted from the log file (rather than just the key for redump-style IRDs).
```cs
IRD ird = new IRD("./game.iso", "./game.getkey.log");
```
Finally, an existing IRD file can be read to create an IRD:
 ```cs
IRD ird = IRD.Read("./game.ird")
```
An IRD can be then be tweaked, its fields printed, and written to a new IRD:
```cs
ird.UID = 0x9F1A51D8;
ird.Print();
ird.Write("game2.ird");
```
