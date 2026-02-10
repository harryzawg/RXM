# RXM
- Was originally just for having a digital SiriusXM tuner essentially but I decided to try to emulate most compatible devices
# Features
- Emulate a ST2 smart tuner with SiriusXM only (No AM/FM tuner)
- Emulate an SMS3/iBridge device (Limited)
- Full preset support for UNO-TS2(d) units
- Favourites for SiriusXM (F1 and F2)
- Full channel/song/artist data on keypads (For SiriusXM, Roon, and SMS3/iBridge)
- Change saved Roon live radio and Sirius channels with the keypad and play/pause them (Only forward/backwards with SMS3)
- API to control volume, source, channel, and power of a zone
# Source naming
- Go into ``RXM/Configuration/RNET.cs`` and scroll to the bottom
- Uncomment/comment the sources you don't use/you do use and rename them to your actual source names (they are zero-based ONLY in this config, so source 0 would be source 1)
# SiriusXM setup
- Install the SXM python library
- Go into RXM/XMGen and make sure your stations.txt file is up to date (by running ``sxm -l``)
- then run the py file and it should automatically generate your ``XM_STATIONS.json`` file
- After completion, place the generated json file where your build exe is
# Roon setup
- Make sure RXM is closed, it bugs out while the extension is not enabled
- Go into RXM/Roon/RoonApi
- Install modules
- Run server.js
- Enable the extension in your Roon app
- Restart RXM
# SMS3/iBridge setup
**THERE IS NO UNO-S2 KEYPAD SUPPORT AT THE MOMENT, AND THERE IS LIMITED/UNSUPPORTED FUNCTIONALITY FOR SOME FEATURES**
- Go into ``RXM/Configuration/Main.cs`` and look for PcConfig and change the BaseUrliBridge to your actual server ip/port
- In Program.cs, change the ``var iBridge = new RXM.Devices.SMS3.SMS3(SM, source: 3);`` line to ``var iBridge = new RXM.Devices.iBridge.iBridge(SM, source: 3);`` only if you want iBridge.
- Then change your source to your actual source number
- Make sure the server is running before starting RXM
- Start RXM, and it should say ``[SMS3] status now=null`` or ``[iBridge] status now=null`` if you have ibridge
- Then, set that source on your UNO-TS2(d) to the iBridge mode type and you should be able to navigate through the menus
# Setting up RXM
- Go into ``RXM/Configuration/Main.cs``, and change your COM port to your actual COM port that is connected to your CAV6.6
- Go into Program.cs, then look for ``Sirius = new Devices.ST2.SXM(SM, 1, PresetManager, Tune: async (i) => await StartChannel(i, null!));``
- Change the source number, which is 1, to your actual Sirius source number
- Start sxm
- Run RXM, and everything should work correctly 
