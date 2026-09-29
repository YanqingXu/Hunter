# Course FMOD bank source

These five `.bank` files contain the unmodified bytes from `Assets/Download/Audio/*.bytes`.
They restore the bank-only source expected by the original FMOD 2.00.04 editor without requiring the author's external `.fspro` project.

FMOD settings use **Single Platform Build**, source `Assets/Plugins/FMOD/Editor/BankSource`, import type **AssetBundle**, and destination `Download/Audio`.
The original FMOD importer reads these banks and maintains the existing runtime `.bytes` assets. The original `YouYou.AudioManager.LoadBanks` continues to load those TextAssets through `FMODUnity.RuntimeManager.LoadBank`.

This directory is below an `Editor` folder and is not a player content source. Do not add these `.bank` files to an AssetBundle or StreamingAssets: the runtime bank set remains `Assets/Download/Audio/*.bytes` only.

When replacing course banks, update this bank source set, including `Master.strings.bank`, then run **FMOD > Refresh Banks**. Retain the runtime `.bytes.meta` files so existing resource references remain valid.
