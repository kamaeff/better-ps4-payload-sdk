# Better PS4 payload SDK

Well... I don't speak C or ASM and don't actually understand all the black magic happening here.  
I was unable to build a little payload for my project with well-known SceneCollective's ps4-payload-sdk.  
Then I found DirectPackageInstaller by marcussacana, and the stuff there just did the job for me.  
At least on my PS4 Slim Baikal 10.01 with the latest GoldHEN.  

So I decided to clone the `Payload/` part of DirectPackageInstaller to be able to use it as SDK detached from DirectPackageInstaller itself.

To get the idea what and how you can do, look at the [example](./example/).  

Also refer to payloads by sleirsgoevy, ps4-payloads-sdk by SceneCollective, OpenOrbis-PS4-Toolchain by OpenOrbis, DirectPackageInstaller payload by marcussacana, and code and writeups by flatz.

License
----------

Original repo was GPLv3 so I keep it GPLv3.


Build
----------

`cd example && make`


Credits (from the original README, unmodified)
----------

-   **LibOrbisPkg** by _maxton_
-   **HttpServerLite** by _jchristn_
-   Payload template by _sleirsgoevy_
-   PS4 OS internals help by _LM_
-   PS4 export definitions by _OpenOrbis SDK_
-   **DirectPackageInstaller** by _marcussacana_
