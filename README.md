# Win32 Dependency Tracker

The aim of this project is to track the likely minimum Windows version which an executable or DLL should load successfully on, based on what DLLs (and functions within those DLLs) will be imported by [load-time linking](https://learn.microsoft.com/en-us/windows/win32/dlls/load-time-dynamic-linking).

### Usage

A path to a DLL/EXE must be provided, as well as the output format (`text` / `json` / `none`):

```
$ ./Win32DependencyTracker.exe -f text "C:/path/to/example.dll"
File: C:/path/to/example.dll
Result: OK
Total symbols checked: 317
Required OS version: Win8 (Win8_RTM, build 9200)

Symbols at that OS version (5):
    advapi32.dll   EventSetInformation         Win8   Win8_RTM   9200
    MFPlat.DLL     MFPutWaitingWorkItem        Win8   Win8_RTM   9200
    MFPlat.DLL     MFLockSharedWorkQueue       Win8   Win8_RTM   9200
    MFPlat.DLL     MFCreateDXGISurfaceBuffer   Win8   Win8_RTM   9200
    MFPlat.DLL     MFPutWorkItem2              Win8   Win8_RTM   9200
```

A maximum expected OS version (or build) can also be specified, which allows the tracker to report failure (including a non-zero exit code) if there are any symbols detected above that OS version:

```
$ ./Win32DependencyTracker.exe -f text --max-expected=Win8_1 "C:/path/to/example.dll"
File: C:/path/to/example.dll
Result: FAIL
Total symbols checked: 344
Required OS version: Win10 (Win10_1507, build 10240)

Symbols at that OS version (2):
    user32.dll     GetDpiForWindow        Win10   Win10_1507   10240
    kernel32.dll   SetThreadDescription   Win10   Win10_1507   10240

Symbols above expected max OS version Win8_1 (2):
    user32.dll     GetDpiForWindow        Win10   Win10_1507   10240
    kernel32.dll   SetThreadDescription   Win10   Win10_1507   10240

$ echo $?
2
```

### How does this work?

We use (modified) parts of the [Dependencies project](https://github.com/lucasg/Dependencies) to obtain all the imported DLLs the specified executable links against, and the functions it uses within those imported DLLs. It then repeats this process recursively, stopping when it reaches DLLs within the system root.

Once this process is completed, we have the set of all the Win32 API functions directly used by this executable and its dependencies. Now we check this set against a reference database containing Win32 functions and the version of Windows they were first made available in, to establish what version of Windows the executable will require to load.

### How do you get this database?

The program downloads the latest ZIP from the [Microsoft Docs SDK API repo](https://github.com/MicrosoftDocs/sdk-api), and parses all API doc files within the ZIP which could be DLL exports (the first run of the program may be quite slow as a result). If you have a local ZIP available, you can pass `--api-doc-zip=path/to/sdk-api-docs.zip`.

It saves the results into a SQLite database in the same folder as the executable, so it only needs to download/read the ZIP once. The symbol cache can be rebuilt by running the program with `--rebuild-symbol-cache` (optionally in conjunction with the ZIP path flag above).

Fortunately Microsoft include (somewhat) structured YAML at the top of their doc sources, so the version information is fairly reliable.

#### … How reliable?

_Fairly reliable._ I've tried to account for the different ways the versions are mentioned in the docs, and there seem to be very few misses - but by the nature of the check, this will always be best-effort. This will never be a cast-iron 100% guarantee that an executable isn't load-time linking against anything new, it should just be a safety check that catches the vast majority of cases.