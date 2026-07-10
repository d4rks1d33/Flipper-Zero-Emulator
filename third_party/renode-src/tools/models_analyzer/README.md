# Renode Models Analyzer
Copyright (c) 2022-2025 [Antmicro](https://www.antmicro.com)

Renode Models Analyzer aims to analyze and extract data from Renode peripheral models, report diagnostics, generate automatic reports and compare peripheral models for best fitness.

This project is divided into two sub-projects: `ModelsAnalyzer`, that contains diagnostic analyzers and is responsible for data extraction and `ModelsCompare` that aims to create reports from data and use the data to compare peripheral models.

## Requirements
In order to compile the application you need to install `.NET SDK 6.0`. You can refer to [official .NET site](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) for installation instructions and packages.

In many Linux distros there already exist .NET packages in official repositories. For example, for Ubuntu you can just use `apt`:
```
apt install dotnet-sdk-6.0
```

## Building

To start using `ModelsAnalyzer` you need to first build Renode. You can get the source code with:
```
git clone https://github.com/renode/renode
```

After you downloaded Renode sources to `renode/` directory, you can run the following commands:
```
cd renode && ./build.sh --net && cd ..
```

To install dependencies needed to build and run Renode on your platform, refer to [the manual](https://renode.readthedocs.io/en/latest/advanced/building_from_sources.html#building-from-source).

Next, you should build this tool:
```
dotnet build
```
or in `Release` configuration:
```
dotnet build --configuration Release
```

## Minimal run command

Below is the simplest way to run analysis on the entire solution:

```
dotnet run --configuration Release --project ModelsAnalyzer/Runner/Runner.csproj \
-- -s renode/Renode_NET.sln --show-summary
```

By default, Runner doesn't display full walkthrough through solution, including files that don't contain peripherals. Still, it has to walk trough the entire solution, to verify which are peripherals and which are not (a peripheral is a class implementing `IPeripheral` interface). To display the entire walkthough you can pass `--no-collapse` option.

`--show-summary` displays analyzers health-checks per each parsed file (whether the analyzer finished with success).

### Dotnet tool
To install Runner as a global [.NET tool](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools), execute script, while inside project's main directory:
```
./Scripts/installRunner.sh
```

You should the location of dotnet tools repository to your `PATH`. On Linux it would be `~/.dotnet/tools` by default. This way you can invoke the runner globally by typing `renode-analysis-runner` in your shell.

To remove the tool run:
```
./Scripts/uninstallRunner.sh
```

### Integration with language server

You can integrate the analyzers with a C# language server that supports Roslyn analyzers. Note that this feature is experimental and might be unstable.

For Omnisharp, you need to enter `Settings` and select `Enable Roslyn Analyzers` and `Analyze Open Documents Only` to `True`.

Then you need to package the `Analyzers` subproject and add the package as a reference in the project where you want to use the analyzers. When opening the project, remember to select it as the active project for Omnisharp. You can use the following commands to build analyzers and add them to your project:
```
cd ModelsAnalyzer/Analyzers
dotnet pack -c Release
dotnet add [path to your *.csproj] package RenodeAnalyzers -s bin/Release
```

## Usage

You can view available commands by typing:
```
$ renode-analysis-runner -h

  -p, --project            Required. Set path to Project.

  -s, --solution           Required. Set path to Solution.

  -a, --analyzers          Set path to analyzers dll.

  --debugger-wait          (Default: false) Wait for debugger to connect.

  -l, --logLevel           (Default: Debug) Set the log level.

  --severity               (Default: Hidden) Set the analyzer global severity level to filter output.

  --analyzers              Run only these analyzers.

  --files                  Analyze only these files.

  --no-collapse            (Default: false) Don't collapse report of traversing projects and files, that were not analyzed.

  -o, --output             (Default: ) Output directory for analysis results.

  --diagnostic-output      (Default: ) Output directory for diagnostic rules, instead of STDOUT.

  --flat-output            (Default: false) Don't put output files into subfolders named after analyzed peripherals. This will break ModelsCompare, so don't use if you want to parse output with ModelsCompare.

  --show-summary           (Default: false) Aggregate and show summary of individual analyzer statuses and documents. It can be resource intensive.

  --help                   Display this help screen.

  --version                Display version information.

```

Most are self-explanatory. You are only required to give either project `-p` or solution `-s` path to parse (they are mutually exclusive), the rest is optional.

* By default, Runner will load analyzers from `RenodeAnalyzers.dll`. They are referenced in the project file, and come prepacked if you decide to install Runner as a dotnet tool. You can give an explicit path with `-a`.

* `--analyzers` specifies a subset of analyzers to load e.g. `---analyzers ResetAnalyzer`.

* `--files` specifies a subset of files to analyze e.g. `--files Potato_UART.cs`. It's case sensitive.

* `--show-summary` displays summary of failed and passed analyzer runs for each parsed file. Note that the results are used to determine analyzers' fitness. They don't aggregate results of analysis (e.g. diagnostic rules), but whether the analyzer was able to parse the peripheral successfully - analyzers themselves report these summaries. It's a health-checking facility.

* `--logLevel` controls output log level (which limits log level of NLog).

*  `--severity` hides diagnostics coming from analyzers, below some level - available are: Error, Warn, Info, Hidden.

* `--output` specifies a folder, where analyzers can store some extra data. One example is `RegistersCoverageAnalyzer` that would output coverage data into the folder. The data is expected to be in a JSON format, one file per file, per analyzer.

* `--diagnostic-output` redirects diagnostics from STDOUT to a folder, with one JSON file per peripheral.

* `--flat-output` doesn't separate results into subfolders. Recommended to not use it, as it might break comparison tool, but in other cases might be easier to investigate results.

### Usage samples - coverage analysis
Usage samples - one a total success, one a partial success, and one a total failure.

For more samples, refer to `.ci.yml`.

* #### Gather specific data from several files

To limit data gathering to a subset of files and analyzers, you can use the following command:

```
dotnet run --project ModelsAnalyzer/Runner/Runner.csproj \
-- -s renode/Renode_NET.sln --severity Info \
--files "Potato_UART.cs" "Litex_UART.cs" "TrivialUart.cs" "AmbiqApollo4_IOMaster.cs" \
 "AmbiqApollo4_Timer.cs" "EOSS3_PacketFifo.cs" "NRF52840_EGU.cs" "AppUart.cs" "GIC.cs" \
--analyzers RegistersDefinitionAnalyzer
```

* #### Get coverage of PotatoUart

This command executes RegistersCoverageAnalyzer on PotatoUART, with `Trace` log level:

```
renode-analysis-runner -s renode/Renode_NET.sln \
--analyzers RegistersCoverageAnalyzer \
--files Potato_UART.cs -l Trace \
--output .
```

Since we are loading the whole solution it will take a while.
You should have now `Potato_UART.cs-registersInfo.json` in your working directory. Its contents can look like this:
```
  {
    "Name": "TransmitRxLo",
    "Address": 0,
    "Width": 32,
    "ResetValue": 0,
    "SpecialKind": "None",
    "CallbackInfo": {
      "HasReadCb": false,
      "HasWriteCb": false,
      "HasChangeCb": false,
      "HasValueProviderCb": false
    },
    "ParentReg": null,
    "Fields": [
      {
        "UniqueId": 0,
        "Range": {
          "Start": 0,
          "End": 31
        },
        "Name": "TransmitRxLo",
        "GeneratorName": "WithValueField",
        "SpecialKind": "None",
        "CallbackInfo": {
          "HasReadCb": false,
          "HasWriteCb": true,
          "HasChangeCb": false,
          "HasValueProviderCb": false
        },
        "FieldMode": "FieldMode.Read | FieldMode.Write",
        "BlockId": 0
      }
    ]
  },
  {
    "Name": "TransmitRxHi",
    "Address": 4,
    "Width": 32,
    "ResetValue": 0,
    "SpecialKind": "None",
    "CallbackInfo": {
      "HasReadCb": false,
      "HasWriteCb": false,
      "HasChangeCb": false,
      "HasValueProviderCb": false
    },
    "ParentReg": null,
    "Fields": [
      {
        "UniqueId": 0,
        "Range": {
          "Start": 0,
          "End": 31
        },
        "Name": "RESERVED",
        "GeneratorName": "WithReservedBits",
        "SpecialKind": "Reserved",
        "CallbackInfo": {
          "HasReadCb": false,
          "HasWriteCb": false,
          "HasChangeCb": false,
          "HasValueProviderCb": false
        },
        "FieldMode": "",
        "BlockId": 0
      }
    ]
  },

  [...]
```

Potato_UART is a simple test case and passes without problems.

* #### Coverage analysis for STM32F1GPIOPort

Get coverage analysis for STM32F1GPIOPort:

```
renode-analysis-runner -s renode/Renode_NET.sln \
--analyzers RegistersCoverageAnalyzer \
--files STM32F1GPIOPort.cs -l Trace \
--output . --show-summary
```

If you see output file, this analysis displays erroneous data: it can't get information about coverage of registers: `ConfigurationLow` and `ConfigurationHigh` since there is a loop involved. This is still work in progress, and in a sample as simple as this, could be at least partially resolved.


* #### Coverage for AmbiqApollo4_GPIO
```
renode-analysis-runner -s renode/Renode_NET.sln \
--analyzers RegistersCoverageAnalyzer \
--files AmbiqApollo4_GPIO.cs -l Trace \
--output . --show-summary
```

This sample is a total failure. Not only the analyzer didn't detect that it deals with cases it cannot parse and reports success, you will see a lot of registers without fields and width:
```
  {
    "Name": "MCUPriorityN0InterruptEnable0",
    "Width": -1,
    "Address": 704,
    "Fields": []
  },
```

This is due to a strange register creation syntax:
```c#
foreach(var descriptor in new[]
{
    new { Register = Registers.MCUPriorityN0InterruptClear0, Type = IrqType.McuN0IrqBank0 },
    new { Register = Registers.MCUPriorityN0InterruptClear1, Type = IrqType.McuN0IrqBank1 },
    new { Register = Registers.MCUPriorityN0InterruptClear2, Type = IrqType.McuN0IrqBank2 },
    [...]
})
{
    descriptor.Register.Define(this)
        .WithFlags(0, PinsPerBank, writeCallback: (bitIdx, _, value) =>
            {
                [...]
            })
        .WithWriteCallback((_, __) => UpdateInterrupt(descriptor.Type));
}
```

The coverage data can be used by `ModelsCompare` to generate layout tables.

# Renode Models Compare

This subproject can be used to parse JSON output from `ModelsAnalyzer`'s analyzers and print it in human-readable format. It can also be used to compare peripherals. Another usage of this tool is to convert between different peripheral representations - e.g. generating SystemRDL models' descriptions from Renode peripheral data.

Please note, that this tool can only be used, if you obtained output from the analyzers, using the `ModelsAnalyzer` tool. Refer to the previous section for usage samples.

## Requirements

To use the tool, you first need to install the required packages.

```
pip3 install -r ModelsCompare/requirements.txt
```

## Minimal run command

A minimal command to generate summary is the following if you have installed the tool as a Python package:

```
renode-models-compare summary data/* --html report.html
```
or enter `ModelsCompare` directory and run:
```
python3 -m RenodeModelsCompare summary data/* --html report.html
```
This command generates HTML report of register layout if `data/` contains output of RegistersCoverageAnalyzer. To obtain the output, refer to the previous section `Usage samples`, describing how to use the `Analysis Runner`.

### Python tool/script

To install the tool as Python package run:
```
./Scripts/installModelsCompare.sh
```

Add pip local storage to your `PATH` (on Linux usually `~/.local/bin/`). This way you can invoke the tool by typing `renode-models-compare` in your shell. The required dependencies should be installed automatically.

To remove the tool run:
```
./Scripts/uninstallModelsCompare.sh
```

## Usage
```
subcommands:
  {summary,compare,misc}
    summary             Print summary of peripheral from JSON/SVD data
    compare             Compare peripheral models
    convert             Convert between file formats
    misc                Misc helper/debugging utils
```

### Generate SystemRDL files

To generate RDL files for a peripheral use:
```
renode-models-compare convert peripheral-data/ --to-systemrdl artifacts/rdls/peripheral.rdl --fill-empty-registers --compact-groups
```

These switches are currently available:
* `--unwind-register-array` - unwind `DefineMany` to separate registers instead of using arrays. This might be necessary, due to limitations in RDL compiler, if evaluation fails later due to interleaving ranges.
* `--fill-empty-registers` - fill registers with no fields, with a dummy R/W field, so the resulting RDL file is legal.
* `--compact-groups` - if there are many Register Groups in a single peripheral (several register enums), put them in one output file, instead of splitting into separate files.

You can verify the correctness of the generated RDL file, by invoking SystemRDL compiler:
```
renode-models-compare misc peripheral.rdl --validate-rdl
```

### Generate report

To generate full report run:
```
renode-models-compare summary data/* --include-diagnostics --html report-full.html
```
Report will contain diagnostics if they have been written by analyzers, and will be printed to `report-full.html`.

You can also generate reports from `svd` files, or convert them to our internal representation. Currently, the only benefit is checking correctness of `svd` parser or inspecting layout manually.
```
python3 -m RenodeModelsCompare convert --from-svd STM32F401.svd:CRC crc-svd.json
python3 -m RenodeModelsCompare summary crc-svd.json --html crc-svd.html
```

### Try find matching peripheral

To find the closest matching peripheral to `USART1` from `STM32F401.svd` run:
```
python3 -m RenodeModelsCompare compare STM32F401.svd:USART1 data/*UART* data/*USART*
```
The tool will try to find the closest match with UARTs and USARTs from the `data` directory. To match it is necessary to have data from `RegistersCoverageAnalyzer` in the `data` directory.

For more usage samples, refer to `.ci.yml`.
