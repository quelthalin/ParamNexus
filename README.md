# ParamNexus
Demon's Souls Param file database built on top of SoulsFormats

Elevator pitch is to create a middle layer between SoulsFormats and apps/users in a more universal way.

Even in the current form, it's been useful for getting a better handle on DeS data (e.g. associating item lots with NPCs).

It's super janky and lots of hardcoding right now, but it can load up Demon's Souls and seems to load DS1, but I'm not familiar enough to ensure quality.

Because it creates a database file, you can load it up into an outside program, e.g. DBeaver as seen here:
![Querying for NPCs associated with item lots](/img/dbeaver_img.png?raw=true)

High level todos:
* Kill all the hardcoding
* Make it easier to interface with
* Load the drawparams and such, too.
* Plenty of refactoring, I'm sure
* potentially expand past DS1
* Clean up C# sins. I don't know what they are yet, but I'm sure I committed them.
