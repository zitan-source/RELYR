# セキュリティーポリシー

[English](SECURITY.md) | [日本語](SECURITY.ja.md)

## サポート対象

セキュリティー修正を検討する対象は、RELYRの最新公開版のみです。報告前に[最新版ページ](https://github.com/zitan-source/RELYR/releases/latest)でバージョンを確認してください。

## 脆弱性は非公開で報告してください

脆弱性の疑いがある問題を公開Issueへ投稿しないでください。非公開マクロ、認証情報、個人情報も記載しないでください。

[GitHubの非公開脆弱性報告フォーム](https://github.com/zitan-source/RELYR/security/advisories/new)を使用し、次の情報をお知らせください。

- RELYRとWindowsのバージョン
- 最短の再現手順
- 想定されるセキュリティー上の影響
- 機密情報を取り除いたログやスクリーンショット

詳細を公開する前に、開発者が再現と修正を行うための時間を設けてください。報告への対応と修正はベストエフォートです。現在、金銭を支払うバグ報奨金制度はありません。

## ダウンロードの確認

RELYRは[公式GitHub Releases](https://github.com/zitan-source/RELYR/releases)からのみ取得してください。各インストーラーには対応するSHA-256ファイルがあります。現在の公開ベータ版はコード署名前のため、Windows SmartScreenに不明な発行元と表示される場合があります。
