# Library for ISO Rebuild Data (LibIRD)

.NET library for generating, writing, and reading IRD files. Functionality is provided for deterministically generating IRDs such that a redump ISO and key have a 1-to-1 correspondence with an IRD file.

## How to use LibIRD

### Reproducible, redump-style IRDs

The standard way of generating a reproducible, redump-style IRD is with a redump ISO file and a redump key file (e.g. http://redump.org/disc/28721/key/):
```cs
byte[] discKey = File.ReadAllBytes("./game.key");
IRD ird = new ReIRD("./game.iso", discKey);
```
Alternatively, the disc key can be extracted from a [ManaGunZ](https://github.com/Zarh/ManaGunZ/) log file:
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
As before, a [ManaGunZ](https://github.com/Zarh/ManaGunZ/) log file can also be used with an ISO to create a custom IRD, with the disc key, disc ID, and PIC all being extracted from the log file (rather than just the key).
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
