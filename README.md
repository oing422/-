# Novelpia Downloader Mobile - Full

원본 WinForms 2.0.3의 기능을 Android용 .NET MAUI 앱 구조로 옮긴 프로젝트입니다.

## 이식한 기능
- 이메일/비밀번호 로그인
- LOGINKEY 직접 입력
- 작품 번호/URL 입력
- EPUB/TXT
- 시작/끝 화 범위
- 공지 포함
- BONUS: 범위 기준 / 항상 포함 / 항상 제외
- 빈 줄 제거
- HTML 유지
- 이미지/표지 다운로드
- EPUB 압축
- 오류 시 중단
- 파일명 작품 번호 포함
- 파일명 화 범위 포함
- 세로쓰기
- 고딕 스타일
- 재시도 횟수
- 동시 작업 수
- 요청 간격
- 폰트 매핑 JSON
- 큐 추가/삭제/일괄 실행
- 진행률/로그
- 중지
- Android 공유 시트를 통한 결과 저장/공유
- 설정 자동 저장

## 왜 ZIP만 바로 실행되지 않나
ZIP은 소스 코드 묶음입니다. Android에서 설치하려면 APK로 컴파일해야 합니다.

현재 이 작업 환경에는 Android SDK/.NET MAUI 빌드 도구가 없어서 APK 바이너리를 직접 생성하지 못했습니다.
대신 아래 두 가지 빌드 방법을 포함했습니다.

### 방법 1: Windows PC
.NET 8 SDK/Android 빌드 환경에서 `build-apk.cmd` 실행.

### 방법 2: GitHub Actions
이 폴더를 GitHub 저장소에 올리고 Actions → Build Android APK → Run workflow.
완료 후 `NovelpiaMobile-APK` artifact 안의 APK를 휴대폰으로 옮겨 설치합니다.

## 참고
- Android의 저장소 정책 때문에 결과 파일은 앱 내부에 만든 뒤 시스템 공유/저장 화면으로 넘깁니다.
- 사이트 구조가 변경되면 HTML/응답 파싱 부분은 수정이 필요할 수 있습니다.
- 이용 권한이 있는 콘텐츠와 사이트 이용약관 범위 안에서 사용하세요.
