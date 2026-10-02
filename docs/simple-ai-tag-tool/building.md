# Building Simple-AI-Tag-Tool

This page covers everything needed to build and run the tool from source: what you need installed, the one-command build, the Visual Studio and command-line routes, a release build, and the two optional servers the AI features talk to. The [README](../../README.md) has the short version; the [user guide](guide.md) covers using the tool.

## What you need

| | Needed for | Notes |
|---|---|---|
| Windows 10 or 11 | everything | The tool is a Windows Forms application. |
| [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer | building | The program targets .NET 8. Any newer SDK builds it (tested with 10.0.300). |
| [Git](https://git-scm.com/downloads) | building | `build.cmd` uses it to fetch the ScreenLister dependency. |
| [Visual Studio 2022](https://visualstudio.microsoft.com/downloads/) | optional | Only if you want to build and debug in the IDE. Install the *.NET desktop development* workload. Visual Studio Code is not enough for the Windows Forms designer. |
| [LM Studio](https://lmstudio.ai/) | AI Refine and AI Chat | Any OpenAI-compatible server works; LM Studio is what the tool is tested with. |
| Python 3.12 (or Anaconda / Miniconda) | the AutoTagger | Only for the original tool's built-in tagger service, *AiApiServer*. |

## How the source is laid out

Three projects take part in a build:

```
<some folder>\
  Simple-AI-Tag-Tool\                  this repository (branch dazzle)
    build.cmd                          the one-command build
    BooruDatasetTagManager.sln         the solution (both projects below)
    BooruDatasetTagManager\            the application (WinForms, net8.0-windows)
      BooruDatasetTagManager.csproj    builds Simple-AI-Tag-Tool.exe
    lib\Dazzle.Layers\                 a small library the application references
    AiApiServer\                       the optional AutoTagger service (Python)
  ScreenLister\                        a sibling clone of starik222/ScreenLister
    ScreenList\ScreenListerNET.csproj  takes frames from videos (net6.0-windows)
```

- **The application** keeps the original project, namespace and solution names (`BooruDatasetTagManager`) so that changes from the original project can still be merged. Only the program file is renamed, to `Simple-AI-Tag-Tool.exe`.
- **`lib\Dazzle.Layers`** holds the data-folder, layering and folder-link code, with no dependency on the application. The application project references it, so it builds automatically; see `lib\VENDORED.md`.
- **ScreenLister** is a separate repository by the original author, used for video frames. The application expects it **next to** this repository, because it references `..\..\ScreenLister\ScreenList\bin\Release\net6.0-windows\ScreenListerNET.dll`. It must be built in **Release** before the application.

## The quick way: build.cmd

From a command prompt in the repository:

```
git clone https://github.com/djdarcy/Simple-AI-Tag-Tool.git
cd Simple-AI-Tag-Tool
git switch dazzle
build.cmd run "C:\path\to\your\dataset"
```

| Command | What it does |
|---|---|
| `build.cmd` | Builds the program (Debug). |
| `build.cmd run` | Builds, then starts it. |
| `build.cmd run "C:\data"` | Builds, then starts it on that dataset folder. |

In order, `build.cmd`:

1. Stops with a message if the program is already running, because Windows locks the exe while it runs and the build could not replace it.
2. The first time only, clones ScreenLister next to the repository and builds it in Release.
3. Builds the application in Debug, showing errors only (see [Warnings](#warnings) below).
4. Prints the path of the program: `BooruDatasetTagManager\bin\Debug\net8.0-windows\Simple-AI-Tag-Tool.exe`.

## In Visual Studio

These are the original project's steps, with ScreenLister added first.

1. Install Visual Studio 2022 with the *.NET desktop development* workload.
2. Clone this repository and switch to the `dazzle` branch.
3. Clone [ScreenLister](https://github.com/starik222/ScreenLister) into a folder **next to** this one, so the two sit side by side. Open `ScreenLister\ScreenList\ScreenListerNET.csproj`, set the configuration to **Release**, and build it (or run `build.cmd` once, which does this step for you).
4. Open `BooruDatasetTagManager.sln` with File > Open > Project/Solution.
5. Build with Build > Build Solution (Ctrl+Shift+B), and start with Debug > Start Debugging (F5). The solution builds `Dazzle.Layers` first because the application references it.

## From the command line, by hand

The same steps without the script:

```
git clone https://github.com/starik222/ScreenLister.git ..\ScreenLister
dotnet build ..\ScreenLister\ScreenList\ScreenListerNET.csproj -c Release
dotnet build BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Debug
BooruDatasetTagManager\bin\Debug\net8.0-windows\Simple-AI-Tag-Tool.exe
```

Use `-c Release` for an optimised build; the program then lands in `bin\Release\net8.0-windows\`. To put the output somewhere else, add `-o <folder>`. That is handy for keeping a second copy while the first one is running.

## A release build to hand to someone

The project is set up for a single-file publish: one compressed `Simple-AI-Tag-Tool.exe` with the .NET runtime inside it, so the person running it needs nothing installed.

```
dotnet publish BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true -o publish
```

- `-f net8.0-windows` is required, because the project file lists its target in the plural (`TargetFrameworks`). Without it the publish stops with error NETSDK1129.
- `--self-contained true` is required too, because the project compresses the single file. A framework-dependent publish stops with error NETSDK1176.
- The result in `publish\` is about 76 MB:
  - the exe (68 MB);
  - a `win-x64\` folder of native graphics DLLs, which the program loads from there at start-up;
  - the `Languages\` and `Translations\` folders;
  - `.pdb` debugging files, which you can leave out.
- Copy the whole folder, not just the exe.

## What the program creates when it runs

The build output holds only the program and its read-only files: the `Languages\` and `Translations\` folders. On its first start the program also creates:

- `ColorScheme.json` and `Tags\` (the autocomplete tag database) beside the exe. These are the original tool's own files.
- **Your data:** settings, recent folders, the AI skills you save, conversations and logs. By default these go in `%USERPROFILE%\.satt`. The program also creates `Documents\Simple-AI-Tag-Tool`, with a link between the two folders. On the very first start it copies any `settings.json` and `recent-folders.json` from the program folder into `.satt` and leaves the originals where they were.
- **Portable instead:** put a file named `portable` beside the exe, and all of this stays in the program folder and nothing is created in your profile. This is useful for a copy on a USB stick, or a second build you test with.

[Where your data lives](guide.md#where-your-data-lives) in the guide has the details.

## Warnings

A build of the application shows about forty compiler warnings and two NuGet warnings (NU1701). They all come from the original project's files and packages, not from anything this fork changed. `build.cmd` hides them and shows errors only.

- **NU1701** says two UI packages, *PagedControl* and *TabControl*, were made for .NET Framework. They work on .NET 8, which is why the original project uses them.
- The compiler warnings are unused variables and fields, nullable annotations outside a nullable context, and two classes that override `Equals` without `GetHashCode`. None of them affects how the program runs.

If you build by hand and want the same quiet output, add both options. The newer .NET terminal logger ignores `-clp`, so `-tl:off` switches back to the classic console logger that honours it:

```
dotnet build BooruDatasetTagManager\BooruDatasetTagManager.csproj -c Debug -tl:off -clp:ErrorsOnly
```

## LM Studio, for AI Refine and AI Chat

1. Install [LM Studio](https://lmstudio.ai/) and download a **vision** model, one whose model card says it accepts images. Qwen vision models work well.
2. Load the model with a context window of about **16k to 32k tokens**. A much larger window can make every request very slow if it does not fit in video memory.
3. Start the server: the Developer tab, then *Start Server*. The default address is `http://127.0.0.1:1234`.
4. In the tool, the address, API key, model name and timeout are under Settings > AutoTagger > OpenAI. The default is `http://127.0.0.1:1234/v1`. Leave the model name empty to use whatever model is loaded.

The tool checks the server before each run and says plainly if no model is loaded or the model cannot see images. The guide's [AI Refine](guide.md#ai-refine-a-local-ai-pass) and [AI Chat](guide.md#ai-chat-an-assistant-that-acts) sections cover using them.

## The AutoTagger service (AiApiServer)

This is the original tool's tag generator, a Python service in the `AiApiServer\` folder. It is only needed for the AutoTagger menu items, not for the AI modes above. These steps are the original project's, kept here so the build instructions are in one place.

**With plain Python:**

```
cd AiApiServer
pip install -r requirements.txt
pip install msvc-runtime
python main.py
```

**With Anaconda or Miniconda**, which the original author recommends if plain Python gives trouble:

```
conda create -n bdtm python=3.12.9
conda activate bdtm
conda install conda-forge::vs2015_runtime
pip install -r requirements.txt
python main.py
```

Later, to start an environment that is already set up, run `conda activate bdtm` and then `python main.py`.

- The `msvc-runtime` or `vs2015_runtime` package supplies the Visual C++ 2015 runtime that recent onnxruntime versions need.
- `requirements.txt` installs PyTorch from the CUDA 12.8 package index, so a recent NVIDIA driver is expected.
- The service listens on port **50051** on **all network interfaces** (`0.0.0.0`). Other machines on your network can reach it unless your firewall blocks the port.
- The tool connects to `http://127.0.0.1:50051` by default.

Model caveats, from the original project:

- Florence2 models need `transformers` 4.49.0: `pip install transformers==4.49.0 --upgrade`. That version may break other models, and `requirements.txt` pins 4.57.3, so choose the one you need.
- `briaai/RMBG-2.0` (background removal) does not work with recent `transformers`. Use the `BiRefNet` models instead.
- `Kwai-Keye/Keye-VL-1_5-8B` needs Flash Attention 2 and triton. Flash Attention has no ready-made Windows build for every setup; see [this thread](https://github.com/Dao-AILab/flash-attention/issues/1469). Install triton with `pip install triton-windows`.

Once the service is running, use the Tools menu to tag every image, the tag button above the tag list to tag the selected images, or the *AutoTagger preview window* tab. Settings > *Auto tagger settings...* holds the generation parameters.

## When something goes wrong

| What you see | Why, and what to do |
|---|---|
| `Simple-AI-Tag-Tool is running. Close it first` | The exe is locked while it runs. Close the program, or build a second copy with `-o <other folder>`. |
| `ScreenListerNET` could not be found | ScreenLister is not next to the repository, or was not built in **Release**. Run `build.cmd` once, or follow step 3 of the Visual Studio route. |
| `error NETSDK1129` when publishing | Add `-f net8.0-windows`. |
| `error NETSDK1176` when publishing | Add `--self-contained true`. |
| The status bar says a newer configuration may exist in `.satt` | A portable copy found settings in `%USERPROFILE%\.satt` too. Nothing was merged; the portable copy uses its own folder. Delete the `portable` file to use `.satt` instead. |
| A pile of warnings | See [Warnings](#warnings). |
