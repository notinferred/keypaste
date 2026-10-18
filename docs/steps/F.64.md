# F.64 — Write the macOS disk image without mounting a volume

Completed 2026-10-18 at `ac4cb27` on `task/f64-dmg-busy`: the dispatched ci 39125458882 and app 39125465297 runs passed every job on all three runners on their first attempt. The integrated commit differs from that tree only in this record.

It had no STEPS row: app 39082595661's first attempt failed at `hdiutil create`, an observation [F.63](F.63.md) recorded, and the founder asked on 2026-10-17 for its root cause rather than a retry, sleep or `sync`.

## Diagnosis

- In that attempt (job 117542506798), `scripts/build-macos-dmg.sh` printed only `hdiutil: create failed - Resource busy`, about ten seconds into the step.
- A probe on `macos-15` (dev 39123475160 at `2e1deaf`) reused that run's bundle, so nothing was built, and wrote the image 40 times as the script did, after the link check the real job runs just before. Run 28 failed. Its verbose output and the system log place the failure: `hdiutil create -srcfolder` attaches a temporary disk, mounts its volume at `/Volumes/keypaste`, copies the folder in, unmounts the volume and ejects the disk. The unmount succeeded, so nothing held a file on the volume; both eject attempts then failed with status `0x10` (EBUSY), and hdiutil returned `Resource busy`. The process holding the disk at the eject was not identified.
- While that volume was mounted, `fs_usage` saw Spotlight's `mds` (about 1,100 operations per image), `fseventsd`, Finder, `storagekitd`, Image Capture's `icdd`, SystemUIServer and others touch it, in each of three traced runs.
- The same probe tried two other ways 25 times each, without a failure. A blank image attached `-nobrowse` at a private mount point and filled with `ditto` kept Spotlight away (2 or 3 operations), but Finder, `fseventsd`, `storagekitd` and `icdd` still touched its volume. `hdiutil makehybrid -hfs` followed by `hdiutil convert` attaches no disk while writing, and no process touched any volume. With a 1-in-40 baseline, 25 clean runs alone do not separate the ways; the case for `makehybrid` is that it has no attach or eject to fail. Each way's first image carried the `Applications` link and the bundle's bytes and modes, and its CLI reported the version.

## What changed

- `build-macos-dmg.sh` writes the image with `hdiutil makehybrid -hfs` from the staged folder and converts it to UDZO (D-0459). The image is now an Apple Partition Map image whose HFS+ volume makehybrid writes, where `create -srcfolder` wrote a GUID-partitioned one. Its checks of the attached image, from the entries to the `--selftest` LaunchServices starts, are unchanged.

## Evidence

- The probe: dev 39123475160, above. It was deleted.
- ci 39125458882 and app 39125465297 at `ac4cb27`, green on all three runners. The app run's `publish + selftest (macos-15)` (job 117669650172) is the first to pass the script's checks on a `makehybrid` image, including the top-level entries check, which counts hidden names.

## Limits and follow-ups

- The script still attaches the finished image to check it and then detaches it, the same unmount and eject whose busy failure is unexplained, and the private-mount traces suggest the same services touch that volume. That detach has not failed in the 60 app runs before F.63's or in this step's runs.
- `makehybrid` is reported to drop extended attributes (LibreOffice moved off it in 2015 for that reason), and codesign keeps the signatures of non-Mach-O files there. Nothing is lost today, since the bundle is zipped without attributes and unsigned, but 3.5b must check the signature on the mounted image, and revisits D-0459 if signing needs attributes kept.
