using RatNav.Service;

// Standalone entry point, for developing the web app without launching the overlay.
// In normal use the WPF app hosts this same service in-process.
var app = ServiceHost.Build(args);

Console.WriteLine($"RatNav service listening on http://localhost:{ServiceHost.DefaultPort}");
app.Run();
