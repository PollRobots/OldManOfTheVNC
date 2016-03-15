Old Man of the VNC
==================

A fork of the original "Old Man of the VNC". This package provides a neat
way of getting VNC working as a UserControl in a WPF environment.

The logic of the VNC connection is encapsulated in OmotVNC.Protocol. There
is a new library called OmotVNC that contains a native WPF control that
enables you to host a VNC connection. It can be used this way:

<Controls:VncHost x:Name="VncHost" Scale="{Binding Scale}" ScaleToFit="{Binding ScaleToFit}" />

In the code-behind you should do as follows:

// start a connection to the VNC server
await VncHost.ConnectAsync(dialog.Server, dialog.Port, dialog.Password);

// update a connection (this should not be necessary)
await VncHost.UpdateAsync();

// stops a connection
await VncHost.DisconnectAsync();


TODO:

Alternative keyboard layouts
Better UI story for touch
Configuration
Pinning connections
Screen capture
etc.