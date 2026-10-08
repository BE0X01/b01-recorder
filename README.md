# b01 recorder · v0.1

Windows용 미니멀 화면 녹화 프로그램. **대상을 선택해도 녹화하지 않습니다. `녹화 시작` 버튼을 눌러야 시작합니다.**

## 다운로드 및 실행

[GitHub Releases](https://github.com/BE0X01/b01-recorder/releases)에서 `b01-recorder-v0.1-win-x64.zip`을 받아 **전체 압축을 풀고** `b01-recorder.exe`를 실행합니다. Windows 10/11 x64용이며 .NET과 FFmpeg를 별도로 설치할 필요가 없습니다. exe 옆의 ffmpeg.exe를 함께 유지하세요.

## 사용법

1. 전체 화면, 윈도우, 영역 지정 중 하나를 선택합니다. 전체 화면은 모든 모니터 또는 개별 모니터를 선택할 수 있습니다. 윈도우는 열린 창 목록에서 선택하고, 영역은 `영역 선택` 버튼을 눌러 드래그합니다. Esc로 영역 선택을 취소합니다.
2. MP4, GIF, WebP 형식과 프레임을 설정합니다. 기본 30 FPS, 60/15/10 FPS 프리셋과 1–120 FPS 직접 설정을 지원합니다.
3. GIF·WebP 품질을 1–100%로 설정합니다. GIF는 팔레트 색상 수와 디더링, WebP는 인코딩 품질에 적용됩니다. MP4는 H.264 고화질로 저장됩니다.
4. MP4에서 `시스템 소리 녹음`을 켜면 Windows 기본 출력 장치의 소리를 녹음합니다. GIF·WebP는 오디오를 지원하지 않아 이 옵션이 비활성화됩니다. 마우스 커서 포함도 선택할 수 있습니다.
5. 저장 폴더를 지정합니다. 기본은 Windows 바탕화면이며, OneDrive 등으로 이동한 바탕화면 경로도 반영합니다.
6. `녹화 시작`을 누르면 앱이 최소화됩니다. 작업 표시줄에서 앱을 열어 `중지 및 저장`을 누르세요. 변환 완료 후 파일 이름을 표시합니다. `저장 폴더 열기`로 결과를 확인합니다. 녹화 중 앱을 닫으면 먼저 녹화를 종료하고 저장합니다.

설정은 `%LOCALAPPDATA%\b01-recorder\settings.json`에 저장됩니다. 다른 FFmpeg 빌드를 쓰려면 `FFmpeg 선택`에서 ffmpeg.exe를 지정합니다. 해당 빌드에는 gdigrab, libx264, libwebp_anim, palettegen/paletteuse 지원이 필요합니다.

## 캡처 및 저장 동작

- 전체 화면과 영역은 FFmpeg gdigrab의 화면 좌표 캡처, 윈도우는 HWND 캡처를 사용합니다. 창을 이동해도 선택한 창을 계속 녹화하지만 **최소화하지 마세요**. 닫힌 창이나 최소화된 창에서는 시작을 거부합니다.
- 시스템 소리는 NAudio WASAPI loopback으로 캡처하고 FFmpeg가 AAC로 합칩니다. 소리가 없는 구간은 무음으로 채워 이후 소리의 위치를 유지합니다. 녹화 중 기본 출력 장치를 변경하지 마세요.
- 임시 H.264 녹화 후 선택한 형식으로 내보냅니다. GIF·WebP는 긴 녹화와 높은 해상도에서 변환 시간과 메모리 사용량이 커질 수 있습니다. GIF 프레임 시간은 10ms 단위라 높은 FPS는 시간 해상도의 제한을 받습니다.
- 홀수 크기 영역은 H.264 호환성을 위해 우측/하단에 최대 1픽셀을 덧붙입니다. 파일 이름에는 시각과 임의 접미사가 있어 기존 파일을 덮어쓰지 않습니다.
- 저장 실패 시 저장 폴더의 `.b01-session-*` 안에 `capture.mkv`와 `audio.wav`를 보존하고 복구 경로를 표시합니다. 정상 저장 후 임시 파일은 삭제합니다.
- DRM 콘텐츠, 독점 전체 화면 게임, 일부 GPU 가속 창은 GDI 캡처에서 검게 보이거나 갱신되지 않을 수 있습니다. 일반 창 모드나 전체 화면/영역 캡처를 사용하세요. 실제 FPS는 시스템 성능에 따라 달라집니다.

## 빌드

Windows와 .NET 10 SDK, PowerShell 7이 필요합니다.

```powershell
./scripts/build.ps1
```

버전과 SHA-256이 고정된 FFmpeg 9.0.2를 내려받고, .NET 런타임을 포함하는 x64 실행 파일과 배포 ZIP, `SHA256SUMS.txt`를 `artifacts/`에 만듭니다.

## 검증

```powershell
./artifacts/b01-recorder-v0.1-win-x64/b01-recorder.exe --verify
python scripts/verify-animations.py
```

실제 데스크톱 화면, 홀수 크기 영역, 프로그램 HWND를 10 FPS로 짧게 녹화합니다. MP4 시스템 소리와 GIF·WebP의 저/고품질 저장을 검사합니다. **이 명령은 실제 화면을 녹화하고 짧은 테스트 톤을 재생합니다.** 결과는 실행 파일 옆 `verification/`에 저장하며 배포 ZIP에는 포함하지 않습니다. 인터랙티브 Windows 데스크톱과 기본 출력 장치가 필요합니다. 프레임 디코딩 검증 스크립트는 Python과 Pillow가 필요합니다.

## 라이선스

프로그램 소스: [MIT](LICENSE). 포함한 FFmpeg/FFprobe와 기타 의존성: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
