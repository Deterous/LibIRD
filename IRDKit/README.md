# How to use IRDKit

IRDKit is a tool that allows direct use of LibIRD functionality from the command line interface.
For full help instructions, run `irdkit help`

## Creating ISOs

For all options, run `irdkit help create`

To create an IRD from an ISO, run `irdkit create game.iso`
Multiple ISOs can be processed at once with `irdkit create game1.iso game2.iso`
Or a whole directory of ISOs can be processed with `irdkit create ./ISO`, or recursively with `irdkit create -r ./ISO`

The IRD will be created in the same folder as the ISO, with the same filename.
A different IRD path and/or filename can be defined with `-o` or `--output=`

### Key
By default, IRDs will be created by pulling keys from redump.org
A key can be manually provided with `-k` or `--key=`
A key file can be provided with `-f game.key` or `--key-file=`
A key from GetKey log file can be used with `-l game.getkey.log` or `--getkey-log=`

### PIC
By default, a PIC will be generated assuming a default layerbreak
A layerbreak value can be provided with `-b` or `--layerbreak=`
A PIC from a GetKey log file can be used with `-l game.getkey.log` or `--getkey-log=`

## Printing info

For all options, run `irdkit help info`

To print info about an ISO or IRD file, run `irdkit info game.iso` or `irdkit info game.ird`
The info can be printed to a file, e.g. `-o out.txt` or `--output=`
The info can be formatted as a JSON with `-j` or `--json`
To print all info in the IRD use `-a` or `--all`

## Comparing IRDs

For all options, run `irdkit help diff`

To compare two IRDs, run `irdkit diff game1.ird game2.ird`
The comparison can be printed to a file, e.g. `-o out.txt` or `--output=`

## Renaming IRDs

For all options, run `irdkit help rename`

To rename an IRD (or folder of IRDs) according to a redump DAT, run `irdkit rename -d redump.dat game.ird`
