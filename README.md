# blog-watcher

指定した情報源を1日1回確認し、キーワードに一致する新着記事をGmailへ通知するC#製のツールです。

職場でC#を使うことになったため、実務に近い構成の.NETアプリケーションを作りながら学習・アウトプットすることも目的としています。

## MVP

- GitHub Actionsで毎日8:00（JST）に実行する
- RSSから記事を取得する
- RSSのタイトル・概要・本文を、大文字小文字を区別せずキーワードで部分一致検索する
- 未通知の記事を1通のプレーンテキストメールにまとめ、自分のGmailへ送信する
- 新着がない日も「新着記事はありません」とメールで通知する
- 既読状態をCloudflare Workers KVに保存する
- 手動実行と、安全なドライランを提供する
- 情報源や取得方式に依存しない共通の記事モデルを使い、将来の取得方式や要約処理の追加に備える

最初に監視する情報源は次の2件です。

| 情報源 | RSSフィード | キーワード |
| --- | --- | --- |
| Mercari Engineering Blog | <https://engineering.mercari.com/blog/feed.xml> | `TiDB` |
| READ UNCOMMITTED | <https://read-uncommitted.com/rss.xml> | `TiDB WEEKLY` |

複数キーワードを設定した場合はOR条件で判定します。監視対象はJSON設定で管理し、RSSの追加ではコード変更を不要にします。

## 実行環境

- .NETのLTS版
- Nix Flake
- direnv
- GitHub Actions

ローカル開発はdirenvを前提とし、初回に`direnv allow`を実行します。`.env.example`を`.env.local`へコピーして認証情報を設定してください。`.env.local`はGit管理外です。GitHub Actionsでは同じ名前のSecretsを設定します。

ローカル実行とGitHub Actionsの手動実行は、デフォルトでドライランにします。明示的に実行モードを指定した場合だけメール送信とKV更新を行います。

```console
# 復元、ビルド、テスト
dotnet restore
dotnet build --no-restore
dotnet test --no-build

# ドライラン（実RSS・実KVを読み取り、副作用なし）
dotnet run --project src/BlogWatcher

# 実行モード
dotnet run --project src/BlogWatcher -- --execute
```

Cloudflare APIトークンには対象KV Namespaceの読み書きに必要な最小権限を付与し、Gmailでは2段階認証を有効にしてアプリパスワードを使用します。

## 今後の候補

MVPには含めませんが、次の拡張を想定しています。

- RSS以外（HTMLやAPIなど）からの記事取得
- OpenRouterなどを経由したLLMによる記事要約
- クレジット不足や障害時の別LLMへのフォールバック
- OSSリリースの監視と要約

詳細な要件、設計方針、障害時の振る舞い、受け入れ条件は[設計ドキュメント](docs/design.md)を参照してください。実サービスを使った確認方法は[実環境動作確認手順](docs/production-verification.md)にまとめています。
