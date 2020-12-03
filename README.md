# URP for Visual Pinball Engine

*Universal Render Pipeline support for the Visual Pinball Engine.*

## Goal

This is an [UPM package](https://docs.unity3d.com/Manual/CustomPackages.html) with a dependency to Unity's
[Universal Render Pipeline](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@10.2/manual/index.html).
If added along with the [VPE package](https://github.com/freezy/VisualPinball.Engine) to a project, VPE will
use this package for dealing with rendering.

It allows us to implement multiple render pipelines without conflicting dependencies.

## Usage

Create a new URP project, import VPE and this package. Don't forget to set `ENABLE_HYBRID_RENDERER_V2`, otherwise
you won't see anything rendered in play mode.

## License

[GPL-3.0](LICENSE)

