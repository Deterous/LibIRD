# Library for ISO Rebuild Data (LibIRD)

.NET library for generating, writing, and reading IRD files. Functionality is provided for deterministically generating IRDs such that a redump ISO and key have a 1-to-1 correspondence with an IRD file.

## What is an IRD?

IRD files contain a summary of what data is on a PlayStation 3 disc. It can be used to rebuild ISOs from files (JB folders). IRD files are also useful for decrypting ISOs after they have been dumped on a PC blu-ray drive, such as for the purposes of legally emulating your PS3 games.

## How to use IRDKit

IRDKit is a tool that allows direct use of LibIRD functionality from a command line interface. The basic usage is:
- Printing info about an ISO: `irdkit info game.iso`
- Printing info about all ISOs and IRDs in a folder: `irdkit info .`
- Creating an IRD from an ISO: `irdkit create game.iso`
- Finding differences between two IRDs: `irdkit diff game1.ird game2.ird`
- Renaming an IRD or folder of IRDs using a redump DAT: `irdkit rename -d redump.dat game.ird`

For detailed usage, read more [here](IRDKit).

## Using the LibIRD library

LibIRD was originally made for creating reproducible, redump-style IRDs when dumping PS3 discs with [MPF](https://github.com/SabreTools/MPF). If you wish to integrate LibIRD into your own application, read the examples [here](LibIRD).
