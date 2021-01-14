# Warden.NET

## What Is It?

Warden.NET is a simple to use library for managing processes and their states. 

## Why?

With [Rainway](https://rainway.com/) we're tasked with launching thousands of different applications from various third parties. To ensure launching games was a smooth process for the user, we needed a reliable way to keep track of game states. 

The ```System.Diagnostics.Process``` class, while useful, does not have a concept for parent applications; In contrast, Windows itself tracks parents; however, it does not track grandparents, and processes can quickly become orphaned. ```Process.EnableRaisingEvents``` while useful, does not support monitoring URI-based launches' lifetime, processes with higher privileges than the calling application, or processes different sessions.

That is why we built Warden.NET.

## Getting Started


_As of Warden.NET 6.0.0 the calling application is no longer required to be running as Administrator. Some processes may be inaccessible however without those privileges._

### Installing

Via Nuget

```
Install-Package Warden.NET
```


### Enable Process Tracking

To initialize Warden to track processes you launch through it you must first call `SystemProcessMonitor.Start(new MonitorOptions());` in the entry point of your application.

If you wish you can optionally subscribe to receive events when all untracked processes have started and stopped execution.  

```csharp
SystemProcessMonitor.OnProcessStarted += (sender, info) => Console.WriteLine(info);
SystemProcessMonitor.OnProcessStopped += (sender, info) => Console.WriteLine(info);
```

### Launching a Process

The `WardenProcess` class allows you to start processes on the current machine in various context. It supports:

- Using the operating system shell to start the process.
- Creating a process as the current interactive user.
- Launching a Microsoft Store / Universal Windows Platform app.

All of these methods support exit events and process family tree tracking. For more information please review the in-line documentation. 


### Impersonation 

Warden supplies a built-in class, `WardenImpersonator`, that helps processes created by `WardenProcess.StartAsUser` execute code as the interactive user. 



## Notes

If you'd like to contribute we'll be happy to accept pull request. You can find a full example application in the repository.
