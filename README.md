# adrapi
Active Directory Rest API

## ABOUT
This is a simple rest api to query and change active directory. It is build with .net core 2.1 and can be run with docker. 

## Requirements 

To develop or use it with the command linet you must have dotnet core 2.1 sdk installed

## How to run 

From the command line simply type: dotnet run

## Configuration

You might want to configure the logging location. Do this by editing the NLog.config file and setting logDirectory to what ever suits you better.

### Security

Since our api has no database we use the security.json to determine witch apiKeys can connect to our system

Basically what you need to configure there is: 

- secretKey: Some random string to work as the Authentication key
- keyID: Unique identifier for the key
- authorizedIP: The IP address authorized to use this key (for now it must be an ip for each key)
- claims: Permission claims we support now the following:
    - isAdministrator -> Determines that the person is an administrator and that it can do everthing 

## Author
Felipe F Quintella 

## License 
Apache License v2.0