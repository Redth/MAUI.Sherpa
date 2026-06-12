// sherpacli.exe myproject.sherpabundle -replacetoken:"Hello=Android World 2" -msbuild:"applicationversion=1.0.0" -variable:"buildNumber=1234" -project:csproj -environment:production -platform:android/ios/windows/etc -step:setup|build|deploy (multiple - what about parallel builds?)
    // action: install, build, deploy OR all OR multiple
// next up: deployments
// builds & versioning
// output variables: ipa/aab file path, version number, 
// infer build - look for global.json OR look at TFM and install latest - then infer XCODE version, Android SDK version, & Workload Set version
{
    "Build":{
        "ReplaceTokens": {
            "SentryDsn": "https://myapp"
        }
    },
    "Production": {
        "ReplaceTokens": {
            "Hello" : "World",
            "ApiBaseUrl": "https://myapi.com",
            "SentryDsn": "https://myapp.production"
        },
        "Android": {
            "Setup":{
                "Keystores:[{
                    "Content": "base64 encoded keystore file",
                    "KeyAlias": "key alias",
                    "KeyPassword": "key password"
                }]
            },
            "Build":{
                "MSBuildProperties":{
                    "ApplicationId": "org.mycompany.myapp",
                    "ApplicationName": "My Name",
                    "ApplicationVersion": "1.0.${buildNumber}", // ${buildNumber} is a variable passed in via command line - ERROR if not provided
                    "MyOwnProperty": "My Own Value"
                },
                "ReplaceTokens":{
                    // ${Hello} - replace with "Android World 2" from command line - should override the value in Build.Variables.Hello and Production.Variables.Hello
                    "Hello": "Android World ${buildNumber}", // command line says that will be overridden to "Android World 2"
                    "FirebaseApiKey": "firebase api key"
                }
            },
            "Deploy":{
                // multiples - firebase, playstore channels, amazon, etc
                "GoogleKey": ""
            }
        },
        "iOS":{
            "Setup":{
                // this should cover widget & standard stuff
                "Profiles":[{
                    "Content": "Base64 value"
                }],
                "Certificates:[{
                    "Content": "base64 encoded certificate",
                    "Password": "certificate password"
                }]
            },
            "Build":{
                "ReplaceTokens":{
                    "Hello": "iOS World"
                }
            },
            "Deploy":[
                {
                    "provider": "TestFlight",
                    "apiKey": "base64 encoded api key"
                }, {
                    "provider": "Firebase",
                    "apiKey": "firebase api key"
                }
                // Allan says testflight only, Jon is P.I.T.A about this
                    // Jon wants useless Firebase - INSERT WHY MEME HERE

            ]
        },
        "MacOS": {
            "ProvisioningProfile": "base64 encoded provisioning profile",
            "Certificate": "base64 encoded certificate",
            "CertificatePassword": "certificate password",
            "Variables":{
                "Hello": "MacOS World"
            }
        },
        "MacCatalyst": {
            "ProvisioningProfile": "base64 encoded provisioning profile",
            "Certificate": "base64 encoded certificate",
            "CertificatePassword": "certificate password",
            "Variables":{
                "Hello": "MacCatalyst World"
            }
        },
        "Windows": {
            "Certificate": "base64 encoded certificate",
            "CertificatePassword": "certificate password",
            "Variables":{
                "Hello": "Windows World"
            }
        }
    },
    "Development": {
    }
}
