# Setting up the project

Clone the repository, open it in Unity, and import four free packages that the repository does not
carry itself. The whole thing takes a few minutes.

**Unity version: 6000.3.10f1.** Other 6.x versions will most likely work, but that is the one the
project is developed and tested against.

## Why some assets are missing

The art and the tweening library come from the Unity Asset Store. They are all free, but the Asset
Store licence does not allow anyone to redistribute the asset files themselves — including by
committing them to a public repository. So the repository carries the project's own code, scenes
and prefabs, and you fetch the rest from the Asset Store with your own Unity account.

The one upside: it keeps the repository around 15 MB instead of roughly 750 MB.

## 1. PrimeTween — installs itself

Nothing to do. `Packages/manifest.json` points at the public npm registry, so Unity downloads
PrimeTween the first time you open the project.

If it does not appear, check that your machine can reach `registry.npmjs.org` and reopen the
project.

## 2. The three art packages — import these yourself

Open each link, sign in, press **Add to My Assets**, then import it in Unity through
**Window → Package Manager → My Assets**.

| Package | What the project uses it for |
|---|---|
| [Low Poly Chess Pack](https://assetstore.unity.com/packages/3d/props/low-poly-chess-pack-50405) | the board and all twelve piece models — **without this you get an empty board** |
| [AllSky Free](https://assetstore.unity.com/packages/2d/textures-materials/sky/allsky-free-10-sky-skybox-set-146014) | the `Epic_BlueSunset` skybox behind the board |
| [Chess Mega Set (Free)](https://assetstore.unity.com/packages/3d/props/chess-mega-set-free-version-287294) | the dark wood material the table surface uses |

Import each one to its default location. The project finds them by Unity's own asset IDs, which are
baked into the packages themselves, so **every reference reconnects on import** — the piece prefabs,
the board material and the scene's skybox all come back on their own. There is nothing to re-wire by
hand.

If you open the project before importing them, a window lists whatever is still missing with a
button through to each store page. You can reopen it any time from
**Chess: The Betrayal → Check Required Assets**.

## 3. Check it worked

Open `Assets/_Scenes/Game Scene.unity` and press Play. You should get a full board with pieces and a
sky.

To run the test suite: **Window → General → Test Runner → EditMode → Run All**. Everything should
pass without the Asset Store packages too, since the tests never touch the art — they only need the
code.
