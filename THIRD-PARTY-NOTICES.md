# Third-party notices

RELYR includes or refers to the following third-party software.
Each component remains subject to its own license. The RELYR MIT
license does not replace these notices.

## Microsoft .NET

RELYR is built as a framework-dependent .NET 10 desktop application. The full
setup bundles Microsoft's official, Authenticode-signed .NET Desktop Runtime
installer and installs it only when required. The lightweight RELYR update
installer does not bundle the runtime.

- Project: https://github.com/dotnet/runtime
- License: MIT

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors
All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

The published Windows payload also resolves the following Microsoft .NET
packages under the same MIT license:

- `System.IO.Pipes.AccessControl` 6.0.0-preview.5.21301.5
- `System.Security.Principal.Windows` 6.0.0-preview.5.21301.5
- `System.IO.FileSystem.AccessControl` 5.0.0
- `System.Management` 10.0.11
- `System.IO.Ports` 10.0.3 and its platform runtime packages, represented in
  the dependency audit as `runtime.*.runtime.native.System.IO.Ports`

## `SharpCompress` 0.49.1

- Project: https://github.com/adamhathcock/sharpcompress
- Package: https://www.nuget.org/packages/SharpCompress/0.49.1
- License: MIT

The MIT License (MIT)

Copyright (c) 2014 Adam Hathcock

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

SharpCompress contains portions derived from other permissively licensed
projects. Its upstream distribution retains the following embedded component
notice:

Copyright (c) 2000-2011 The Legion Of The Bouncy Castle

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## LibreHardwareMonitorLib 0.9.6

RELYR uses the unmodified LibreHardwareMonitorLib package in an isolated
sensor process to read hardware telemetry exposed by supported devices.

- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Package: https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6
- License: Mozilla Public License 2.0
- License text: https://www.mozilla.org/MPL/2.0/

Libre Hardware Monitor is copyright (c) 2010-2026 Michael Möller and
LibreHardwareMonitor contributors. The distributed library remains subject to
the Mozilla Public License 2.0 and its upstream third-party notices. RELYR does
not modify the library.

The LibreHardwareMonitor package brings the following libraries into the
Windows distribution. The listed links identify the exact corresponding source
revisions. Each MPL-covered library is distributed unmodified; its source form
remains available under MPL-2.0 at the linked revision.

- `LibreHardwareMonitorLib` 0.9.6 — MPL-2.0 —
  [source revision 3d331e3](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701)
- `BlackSharp.Core` 1.0.7 — MPL-2.0 —
  [source revision c70b735](https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0)
- `DiskInfoToolkit` 1.1.2 — MPL-2.0 —
  [source revision 25319ea](https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea)
- `RAMSPDToolkit-NDD` 1.4.2 — MPL-2.0 —
  [source revision 3b47b96](https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce)

MPL-2.0 license text: https://www.mozilla.org/MPL/2.0/

## HidSharp 2.6.4

`HidSharp` is an unmodified transitive dependency of LibreHardwareMonitorLib.

- Project: https://software.seekye.com/hidsharp
- Package: https://www.nuget.org/packages/HidSharp/2.6.4
- License: Apache License 2.0
- Distributed license copy: `licenses/HidSharp-LICENSE.txt`

Copyright 2010-2025 James F. Bellinger.

## Mono.Posix.NETStandard 1.0.0

`Mono.Posix.NETStandard` and its native helper are unmodified transitive
dependencies of LibreHardwareMonitorLib.

- Package: https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0
- Source: https://github.com/mono/mono/tree/main/mcs/class/Mono.Posix
- License: MIT (publisher package license link and Mono project license)
- License: https://github.com/mono/mono/blob/main/LICENSE
- Copyright: Microsoft Corporation and Mono contributors

## Microsoft Windows SDK .NET projections

RELYR uses the official Microsoft Windows SDK .NET projections to read Windows
APIs. The framework-dependent Windows payload includes
`Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` supplied through the
.NET SDK targeting pack.

- Project: https://github.com/microsoft/CsWinRT
- License: MIT / Microsoft SDK redistribution terms as supplied with the .NET SDK
- Publisher: Microsoft Corporation

## Inno Setup

The optional RELYR installer is generated with Inno Setup. The Inno Setup
compiler is a build-time tool and is not distributed with RELYR.

- Project: https://github.com/jrsoftware/issrc
- Website: https://jrsoftware.org/
- License: Inno Setup License

Except where otherwise noted, all of the documentation and software included
in the Inno Setup package is copyrighted by Jordan Russell.

Copyright (C) 1997-2026 Jordan Russell. All rights reserved.
Portions Copyright (C) 2000-2026 Martijn Laan. All rights reserved.

This software is provided "as-is," without any express or implied warranty.
In no event shall the author be held liable for any damages arising from the
use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter and redistribute it, provided
that the following conditions are met:

1. All redistributions of source code files must retain all copyright notices
   that are currently in place, and this list of conditions without
   modification.
2. All redistributions in binary form must retain all occurrences of the above
   copyright notice and web site addresses that are currently in place (for
   example, in the About boxes).
3. The origin of this software must not be misrepresented; you must not claim
   that you wrote the original software. If you use this software to
   distribute a product, an acknowledgment in the product documentation would
   be appreciated but is not required.
4. Modified versions in source or binary form must be plainly marked as such,
   and must not be misrepresented as being the original software.

Jordan Russell

https://jrsoftware.org/

## VirtualDesktopAccessor.dll

- Project: https://github.com/Ciantic/VirtualDesktopAccessor
- Official release: `2024-12-16-windows11`
- Distributed file SHA-256: `8740C572A1C000E3B87FFEB1E4C397EAE9AF3BD4A2ABDC3BCFFACAB4493F8FF5`
- License: MIT

Copyright (c) 2015-2023 Jari Otto Oskari Pennanen

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## SylphyHornPlusWin11

Virtual desktop behavior was designed with reference to this project. Its
binary is not distributed with RELYR. This notice is retained for
attribution of any implementation portions derived from that project.

- Project: https://github.com/hwtnb/SylphyHornPlusWin11
- License: MIT

The MIT License (MIT)

Copyright (c) 2015-2018 Manato KAMEYA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
