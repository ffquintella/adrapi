# adrapi
Active Directory Rest API

## ABOUT
This is a simple rest api to query and change active directory. It is build with .net core 2.1 and can be run with docker. 

## Requirements 

To develop or use it with the command linet you must have dotnet core 2.1 sdk installed

## How to run 

From the command line simply type: dotnet run

### Consuming the API

To be able to consume the api you will need an apikey (see bellow) and define an api version 

#### Versioning 

The api is versioned through passing a header called api-version with a number. The header is mandatory and failing to set it will result on a error message.

The valid values are:
 - 1.0 -> first api version (12-2018)
 
#### API Key

 There must be a header called api-key witch is created with *key-ID:secretKey* 

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
    - isMonitor -> Can read most of things
    
**WARNING** Change the security.json file or your api will be open!    

## API

- GET - /api/users - List all users 
    - _full = bool - Returns a full list
- GET - /api/users/:user - Get the user details       
            
## Author
Felipe F Quintella 

## License 
Apache License v2.0