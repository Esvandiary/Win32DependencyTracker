# Win32 Dependency Tracker

The aim of this project is to track the likely minimum Windows version which an executable or DLL should load successfully on, based on what DLLs (and functions within those DLLs) will be imported by [load-time linking](https://learn.microsoft.com/en-us/windows/win32/dlls/load-time-dynamic-linking).

### Usage

TODO

### How does this work?

We use parts of the [Dependencies project](https://github.com/lucasg/Dependencies) to obtain all the imported DLLs the specified executable links against, and the functions it uses within those imported DLLs. It then repeats this process recursively, stopping when it reaches DLLs within the system root.

Once this process is completed, we have the set of all the Win32 API functions directly used by this executable and its dependencies. Now we check this set against a reference database containing Win32 functions and the version of Windows they were first made available in, to establish what version of Windows the executable will require to load.

### How do you get this database?

The program downloads the latest ZIP from the [Microsoft Docs SDK API repo](https://github.com/MicrosoftDocs/sdk-api), and parses all API doc files within the ZIP which could be DLL exports. It saves the results into a SQLite database in the same folder as the executable, so it only needs to download/read the ZIP once. Fortunately Microsoft include (somewhat) structured YAML at the top of their doc sources, so the version information is fairly reliable.

#### … How reliable?
_Fairly reliable._ I've tried to account for the different ways the versions are mentioned in the docs, and there seem to be very few misses, but this will never be perfect. This will never be a cast-iron 100% guarantee that an executable isn't load-time linking against anything new, it should just be a safety check that catches the vast majority of cases.