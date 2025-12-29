# Changelog

All notable changes to this project will be documented in this file.

## [1.0.1] - 2025-12-28
- Fixed an issue where changelog dialog show up every time.
- Avoid throwing exception when registering duplicate key bindings, return false instead.
- Refactored key binding framework to use flag-based context filtering.
- Adjust OptionsPanelBase settings file access.
- Added AttributeExtensions extension methods.
- Add NormalButtonsCard and INormalButtonsCard for buttons management.
- Add ValueTuple structs.
- Add Lazy<T> class and LazyThreadSafetyMode enum for lazy initialization

## [1.0.0] - 2025-12-07
- Added mod translation button, mod page button, get help button, log level setter to option panel advanced page.
- Added key binding reset function.
- Added translation progress function.
- Added mod status function.
- Optimized mod compatibility monitoring function.
- Optimized localization function.
- Optimized serialization and deserialization.
- Optimized reset mod function.
- Updated UI Framework.
- Fixed an issue where the in-game tool button did not show up.
- Rewrote the compatibility manager.