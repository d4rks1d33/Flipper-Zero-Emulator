# Inpli TFTP Server library for .NET

## Introduction

This is a trivial library for a trivial protocol.

This is an implementation of the TFTP protocol as specified by RFC1350 for the purpose of
handing file transfer requests. It has been interoperability tested against Cisco IOS and
is being developed for use within an internal product. At this time, it is not fully async,
but is being developed to become such as needed.

## Usage

The protocol is run as a singleton and is meant to be left running. The usage is pretty simple,
just start the instance after setting event handlers and it's done.

There are four events that can be implemented to make this library work.

### FileReceived

This event is triggered when a file is received by the server.

### FileTransmitted

This event is triggered when a file transmission is completed

### Log

This event is triggered when logging is desired. Logging verbosity is set based on the
property LogSeverity.

### GetStream

This is an async event which gets triggered when a read request comes in from a client.
The "Result" field returned from this event contains a stream object to be read in order
to transmit the file.