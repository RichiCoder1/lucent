using System;
using Avalonia;

var app = new ConsumerApp();
app.Styles.Add(new global::Lucent.Styles.Utilities.LucentStyles());
Console.WriteLine("utility package installed");

sealed class ConsumerApp : Application { }
