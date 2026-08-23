# 実環境動作確認手順

この手順書では、実際のRSS、Cloudflare Workers KV、Gmail SMTP、GitHub Actionsを使ってblog-watcherを確認する。最初に副作用のないドライランを行い、その後に明示的な実行モードへ進む。

## 1. 確認する動作

- 2件の実RSSを取得できる
- 実KVから初期化状態と既読状態を読み取れる
- ドライランではメール送信もKV書き込みも行われない
- 初回の実行モードでは一致済みの記事をKVへ登録し、「新着記事はありません」というメールを1通送る
- 2回目以降は未読の一致記事だけを1通にまとめ、メール送信成功後にKVへ登録する
- GitHub Actionsの手動実行と定期実行が動作する

## 2. 事前条件

- Nixとdirenvがインストールされている
- Cloudflareアカウントを利用できる
- 2段階認証を有効にした個人Gmailアカウントを利用できる
- 対象リポジトリのGitHub Actions Secretsを設定できる

Google Workspaceなどの組織管理アカウントでは、管理ポリシーによってアプリパスワードを作成できない場合がある。その場合は個人Gmailアカウントを使用するか、管理者へ確認する。

## 3. ローカル環境の確認

リポジトリのルートで次を実行する。

```console
direnv allow
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
dotnet format --no-restore --verify-no-changes
```

次をすべて満たせば事前確認は完了である。

- ビルドが警告・エラーなしで完了する
- 全テストが成功する
- フォーマット違反がない

## 4. Cloudflare Workers KVの準備

### 4.1 Namespaceを作成する

1. Cloudflare DashboardでWorkers KVを開く。
2. 動作確認専用のNamespaceを作成する。例: `blog-watcher-verification`
3. Account IDとNamespace IDを控える。

既存の本番Namespaceと状態を混在させないため、初回の確認には専用Namespaceを推奨する。

### 4.2 APIトークンを作成する

1. Cloudflareの「My Profile」から「API Tokens」を開く。
2. カスタムトークンを作成する。
3. Account権限として`Workers KV Storage: Edit`を指定する。
4. Account Resourcesをblog-watcherで使用するアカウントだけに制限する。
5. 必要であれば有効期限や送信元IP制限も設定する。
6. 表示されたトークンを安全な場所へ一時保存する。後から再表示はできない。

CloudflareがNamespace単位のリソース制限を提供していない場合は、対象アカウントだけへ範囲を限定し、blog-watcher専用トークンとして管理する。公式の権限一覧は[Cloudflare API token templates](https://developers.cloudflare.com/fundamentals/api/reference/template/)を参照する。

## 5. Gmailの準備

1. 送受信に使うGoogleアカウントで2段階認証を有効にする。
2. Googleアカウントの「アプリ パスワード」を開く。
3. blog-watcher用のアプリパスワードを作成する。
4. 表示された16桁のパスワードを安全な場所へ一時保存する。

通常のGoogleアカウントパスワードは使用しない。アプリパスワードの条件や作成できない場合の説明は[Google公式ヘルプ](https://support.google.com/accounts/answer/185833?hl=ja)を参照する。

## 6. ローカルの秘密情報を設定する

`.env.example`を`.env.local`へコピーする。

```console
cp .env.example .env.local
```

`.env.local`へ次の値を設定する。

```dotenv
CLOUDFLARE_ACCOUNT_ID=<CloudflareのAccount ID>
CLOUDFLARE_KV_NAMESPACE_ID=<確認用Namespace ID>
CLOUDFLARE_API_TOKEN=<Cloudflare APIトークン>
GMAIL_ADDRESS=<送信元兼送信先のGmailアドレス>
GMAIL_APP_PASSWORD=<Gmailアプリパスワード>
```

設定後、新しいシェルを開くか次を実行してdirenvへ再読込させる。

```console
direnv reload
```

次の点を確認する。

- `.env.local`が`git status`へ表示されない
- 秘密情報をターミナルへ表示するコマンドを実行していない
- `.env.local`をコミット、画面共有、ログ添付しない

## 7. ローカルでドライランする

次のコマンドは実RSSと実KVを読み取るが、メール送信とKV書き込みは行わない。

```console
dotnet run --project src/BlogWatcher --configuration Release
```

正常時はログで次を確認する。

- `mercari-engineering`と`read-uncommitted`の取得が成功している
- 各監視元の`FetchedCount`、`MatchedCount`、`SeenCount`、`NewCount`、`ExcludedCount`が出力される
- `Dry run`と表示される
- 終了コードが`0`である

さらに、次を外部側で確認する。

- Gmailにblog-watcherからのメールが届いていない
- 確認用KV Namespaceにキーが追加されていない

ドライランでエラーになった場合は実行モードへ進まない。

## 8. ローカルで初回の実行モードを確認する

この操作はメールを1通送信し、KVへ書き込む。確認用Namespace IDであることを再確認してから実行する。

```console
dotnet run --project src/BlogWatcher --configuration Release -- --execute
```

### 8.1 Gmailで確認する

- 件名が`[blog-watcher] 新着0件 - YYYY-MM-DD`である
- 本文の日付がJST基準である
- 本文に`新着記事はありません`が含まれる
- プレーンテキストメールである
- 送信元と送信先が`GMAIL_ADDRESS`と一致する

### 8.2 KVで確認する

確認用Namespaceに次のキーが作成されていることを確認する。

- `initialized:mercari-engineering`
- `initialized:read-uncommitted`
- キーワードに一致した記事ごとの`seen:<SHA-256ハッシュ>`

`initialized:*`の値には`InitializedAt`と`SourceId`、`seen:*`の値には`FirstSeenAt`、`SourceName`、`Url`が含まれる。秘密情報は保存されない。

初回実行では、既存の一致記事を通知せず既読として登録するため、新着0件のメールになるのが正しい。

## 9. 2回目の実行を確認する

同じコマンドをもう一度実行する。

```console
dotnet run --project src/BlogWatcher --configuration Release -- --execute
```

RSSに新しい一致記事が追加されていなければ、再び「新着記事はありません」というメールが1通届き、既存の`seen:*`キーは増えない。新しい一致記事がある場合は、1通のメールに記事名、監視元、URL、取得できた公開日時がまとまり、送信成功後に対応する`seen:*`キーが追加される。

### 任意: 新着通知経路をすぐ確認する

この確認は、必ず使い捨て可能な確認用Namespaceでだけ行う。

1. KV Dashboardで通知対象にしたい`seen:*`キーを1件選び、値の`Url`を控える。
2. そのキーだけを削除する。
3. 実行モードを再度実行する。
4. 控えたURLの記事が新着メールに含まれることを確認する。
5. メール成功後、削除した`seen:*`キーが再作成されることを確認する。

これにより、未読判定、メール作成、SMTP送信、送信後の既読保存を一連で確認できる。本番Namespaceや複数キーを対象にしない。

## 10. GitHub Actions Secretsを設定する

GitHubリポジトリの「Settings」→「Secrets and variables」→「Actions」で、次のRepository secretsを登録する。

- `CLOUDFLARE_ACCOUNT_ID`
- `CLOUDFLARE_KV_NAMESPACE_ID`
- `CLOUDFLARE_API_TOKEN`
- `GMAIL_ADDRESS`
- `GMAIL_APP_PASSWORD`

GitHub CLIを使う場合は、値をコマンドライン引数へ直接書かず、次を1件ずつ実行して対話入力する。

```console
gh secret set CLOUDFLARE_ACCOUNT_ID
gh secret set CLOUDFLARE_KV_NAMESPACE_ID
gh secret set CLOUDFLARE_API_TOKEN
gh secret set GMAIL_ADDRESS
gh secret set GMAIL_APP_PASSWORD
```

GitHub Secretsの仕組みは[GitHub公式ドキュメント](https://docs.github.com/actions/concepts/security/secrets)を参照する。

## 11. GitHub Actionsで確認する

変更をデフォルトブランチへ反映した後、GitHubの「Actions」から`Watch blogs`を選択する。

### 11.1 手動ドライラン

1. 「Run workflow」を選択する。
2. `dry_run`が`true`であることを確認する。
3. Workflowを実行する。
4. ログでRSS取得、KV読取、判定結果、`Dry run`を確認する。
5. メールが届かず、KVが更新されていないことを確認する。

### 11.2 手動の実行モード

1. 「Run workflow」を選択する。
2. `dry_run`を`false`へ変更する。
3. 副作用が発生することを理解したうえでWorkflowを実行する。
4. Workflowが成功し、メール受信とKV更新がローカル確認時と同じになることを確認する。

### 11.3 定期実行

`.github/workflows/watch.yml`のcronは`0 23 * * *`、つまり毎日23:00 UTC（翌日8:00 JST）である。スケジュール実行は常に実行モードになる。GitHub Actionsの混雑により開始が遅れる場合がある。

## 12. 合格チェックリスト

- [ ] Releaseビルド、テスト、フォーマット確認が成功した
- [ ] ローカルドライランで2件のRSS取得とKV読取に成功した
- [ ] ドライランでメール送信とKV書き込みが発生しなかった
- [ ] 初回の実行モードで新着0件メールが届いた
- [ ] 2件の`initialized:*`キーが作成された
- [ ] 一致済み記事の`seen:*`キーが作成された
- [ ] 2回目の実行で既読記事が再通知されなかった
- [ ] GitHub Actionsの手動ドライランが成功した
- [ ] GitHub Actionsの手動実行モードでメールとKV更新を確認した
- [ ] 定期実行の時刻が毎日8:00 JST相当になっている
- [ ] ログやリポジトリに秘密情報や記事本文全体が出ていない

## 13. トラブルシュート

| 症状 | 確認事項 |
| --- | --- |
| 起動時に環境変数不足で失敗する | `.env.local`の変数名、空欄、`direnv reload`の実行を確認する |
| KVが`401`または`403`になる | Account ID、Namespace ID、APIトークン、`Workers KV Storage: Edit`権限、対象Accountを確認する |
| RSS取得が失敗する | 対象URLへ接続できるか、DNS、TLS、プロキシ、GitHub Actionsの障害状況を確認する |
| Gmail認証に失敗する | 通常のパスワードではなくアプリパスワードを使っているか、2段階認証、アドレスを確認する |
| メールが見つからない | 迷惑メール、送信済み、受信トレイのフィルタを確認する |
| 初回メールが新着0件になる | 仕様どおり。既存記事は通知せずKVへ既読登録する |
| 同じ記事がまれに再通知される | メール成功後からKV保存前の障害ではat-least-once保証により再送され得る |
| Actionsの手動実行ボタンがない | Workflowがデフォルトブランチに存在し、Actionsが有効か確認する |

障害調査であっても、APIトークン、アプリパスワード、メールアドレスをIssueやActionsログへ貼り付けない。認証情報が露出した可能性がある場合は、Cloudflare APIトークンとGoogleアプリパスワードを失効させて再発行する。
