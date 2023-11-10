# Library for ISO Rebuild Data (LibIRD)

This .NET library provides methods for generating, writing, and reading IRD files. Functionality is provided for deterministically generating IRDs such that a redump ISO and key have a 1-to-1 correspondence with an unique IRD file.

## How to use LibIRD

### Reproducible, redump-style IRDs

The standard way of generating a reproducible, redump-style IRD with LibIRD is with a redump ISO file and a redump key file (e.g. http://redump.org/disc/28721/key/):
```
byte[] discKey = File.ReadAllBytes("./game.key");
IRD ird = new ReIRD("./game.iso", discKey);
```
alternatively, the disc key can be extracted from a [ManaGunZ](https://github.com/Zarh/ManaGunZ/) .getkey.log file:
```
IRD ird = new ReIRD("./game.iso", "./game.getkey.log");
```

### Custom IRDs

Functionality is also provided for creating custom IRDs, with whatever data you want. One way is to use an ISO with a custom disc key, disc ID, and PIC:
```
byte[] discKey = new byte[16];
byte[] discID = new byte[16];
byte[] PIC = new byte[115];
// Set vars to desired values
IRD ird = new IRD("./game.iso", discKey, discID, discPIC);
```
As before, a [ManaGunZ](https://github.com/Zarh/ManaGunZ/) log file can also be used with an ISO to create an IRD, with the disc key, disc ID, and PIC all being extracted from the .getkey.log file.
```
IRD ird = new IRD("./game.iso", "./game.getkey.log");
```
Finally, an existing IRD file can be read to create an IRD:
 ```
IRD ird = IRD.Read("./game.ird")
```

An IRD can be then be tweaked, its fields printed, and written to a new IRD:
```
ird.UID = 0x9F1A51D8;
ird.Print();
ird.Write("game2.ird");
```
