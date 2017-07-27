# Warden 

## What Is It?

Warden.NET is a simple to use library for managing processes and their states. 

## Why?

With [Rainway](https://rainway.io) we're tasked with launching thousands of different applications from various third parties. To ensure launching games was a smooth process for the user, we needed a reliable way to keep track of game states. 

The ```System.Diagnostics.Process``` class while useful does not have a concept for parent applications; while Windows itself does track parents, it does not track grandparents and processes can quickly become orphaned. Which is where Warden comes in.  



## Getting Started

### Installing

Via Nuget

```
Install-Package Warden.NET
```

### Initializing Warden 
To initialize Warden inside your application, call the static initialization function. (Pass ```true``` if you'd like Warden to kill all monitored processes on exit.)

```csharp
WardenManager.Initialize();
```

### Launching a Process

Warden processes are launched asynchronously and return null if it fails to launch.

If you wish to launch a Win32 application just call the following code. 

```csharp
var process = await WardenProcess.Start("G:/Games/steamapps/common/NieRAutomata/NieRAutomata.exe", string.empty, ProcessTypes.Win32);
```

Similarly, you can launch a UWP like so.

```csharp
 var process = await WardenProcess.Launch("Microsoft.Halo5Forge_8wekyb3d8bbwe", "!Ausar", ProcessTypes.Uwp);
```

If you need to start a URI, that is also supported. 

Pass the URI you wish to run, as well as the full path to the executable that should appear afterwards. This method will return an "empty" Warden object. The ```Id``` of the object will update automatically when the target process launches.


```csharp
 var test = await WardenProcess.StartUri("steam://run/107410", "G:\\Games\\steamapps\\common\\Arma 3\\arma3launcher.exe", string.Empty);
```

Finally, you can build a Warden process tree from an already running process like so

```
 WardenProcess.GetProcessFromId(999);
```

### Listening to Process States

You can subscribe to the ```OnStateChange``` event to know when a process state has updated. Additionally you can subscribe to ```OnChildStateChange``` to know when its children have had a change in state.


## Notes

If you'd like to contribute we'll be happy to accept pull request. You can find a full example application in the repository.

