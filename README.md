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

Here there is a short description of the api. But you can also get the documentation running the program and accessing /swagger on it.

### Users

- GET - /api/users - List all users 
    - _full (bool) - Returns a full list
    - _start (int) - Results index to start form
    - _end (int) - Results index to end form
- POST - /user/authenticate - Returns 200 if OK 400 if login or password is not present and 401 if password is wrong
    - Body json:
        - String: login
        - String: password
- GET - /api/users/:user - Gets the user's details  
- PUT - /api/users/:user - Creates a new user (1)  
    - Body json:
        - String: name
        - String: login
        - String: description (optional)
        - String: password (optional)
        - Boolean: IsDisabled (optional)
        - Boolean: IsLocked (optional)
        - Boolean: PasswordExpired (optional)
        - Array(String): memberof - List of DNs
- DELETE - /api/users/:user - Deletes the user
- POST - /user/:user/authenticate - Returns 200 if OK 404 if user not found and 401 if password is wrong
    - Body json:
        - String: password
- GET - /api/users/:user/exists - Returns code 200 if true and 404 if false.
- GET - /user/:user/member-of/:group - Returns code 200 if true, 404 if user is not found and 250 if not member.



#### Observations

* (1) It's only possible to create users with passwords with an active directory configured to use SSL

### Groups

- GET - /api/groups - List all groups
    - _full (bool) - Returns a full list
    - _start (int) - Results index to start form
    - _end (int) - Results index to end form
- GET - /api/groups/:group - Gets the group's details
- PUT - /api/groups/:group - Creates a new group  
    - Body json:
        - String: name
        - String: dn
        - String: description (optional)
        - Array(String): member - List of DNs
- GET - /api/groups/:group/exists - Returns code 200 if true and 404 if false.
- GET - /api/groups/:group/members - Returns a list of the DNs of the groups members.
- PUT - /api/groups/:group/members - Returns a list of the DNs of the groups members.
    - Body json:
        - Array(String): member - List of DNs
        
### OUs

- GET - /api/ous - List all ous
- PUT - /api/ous - Creates a new OU
    - Body json:
        - String: name
        - String: dn
        - String: description (optional)
- GET - /api/ous/:ou - Gets the ou's details     
- GET - /api/ous/:ou/exists - Returns code 200 if true and 404 if false.                     
                                                   
## Author
Felipe F Quintella 

## License 
Apache License v2.0