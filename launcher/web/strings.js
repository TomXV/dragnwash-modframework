'use strict';
/* Every piece of text on the launcher page, in English and Japanese.
   Plain strings, or small functions for the ones that take numbers or names.
   The install log and the failure details stay English (they come from the launcher, like the installer's log). */

// "A and B", "A, B and C" / "A と B", "A、B、C"
function joinNames(names, lang) {
  if (lang === 'ja') return names.length === 2 ? names.join(' と ') : names.join('、');
  if (names.length < 2) return names.join('');
  return names.slice(0, -1).join(', ') + ' and ' + names[names.length - 1];
}

const STRINGS = {
  en: {
    minimize: 'Minimize',
    close: 'Close',

    // header state, one per board
    stateList: 'Updates available',
    stateUpdating: 'Updating',
    stateRestart: 'Update and restart',
    stateFailed: "Couldn't update",

    // 0: the line under the logo
    introChecking: 'Checking for updates',
    introFound: 'Updates found',
    introNone: 'No updates',
    introStarting: 'Starting the game',

    // 1: the updates list
    listTitle: (n) => (n === 1 ? '1 mod has an update' : `${n} mods have updates`),
    listLead: (when) => 'Pick the ones you want. This is what the game found last time you played' + (when ? ` (${when}).` : '.'),
    installMod: (name) => `Install ${name}`,
    cantHere: "can't be installed from here",
    openRelease: 'Open release page',
    selected: (n, size) => `Selected: ${n}` + (size ? ` (${size})` : ''),
    whatsNew: "What's new",
    notesLabel: 'Release notes',
    skipVersion: 'Skip this version',
    cantNote: "This mod can't be updated from here. Get it from the release page.",
    notesNone: 'No release notes were written for this version.',
    notesCut: '(cut short — see the release page)',
    netList: 'Goes online only when you press “Update and play”, and only to github.com and release-assets.githubusercontent.com. Otherwise it connects nowhere.',
    playNoUpdate: 'Play without updating',
    updateAndPlay: 'Update and play',

    // 2 and B: updating
    updTitle: 'Updating',
    updLead: (items) => (items.length > 3
      ? `Installing ${items.length} updates, then starting the game.`
      : `Installing ${joinNames(items, 'en')}, then starting the game.`),
    restartTitle: 'The game is closed. Updating',
    restartLead: "When it's done, the game starts again by itself.",
    netUpdating: 'Nothing is written to the game folder until the checks pass, so Cancel leaves it as it was.',
    netRestart: (backup) => `Backup: ${backup} · Goes online only to github.com and release-assets.githubusercontent.com.`,
    cancel: 'Cancel',
    logLabel: 'Install log',

    stepWait: 'Wait for the game to close',
    stepDl: 'Download',
    stepChk: 'Check size and SHA-256',
    stepBak: 'Back up',
    stepIns: 'Install',
    stepGo: 'Start the game',
    stepGoAgain: 'Start the game again',
    smallDl: (n, size) => `${n} ${n === 1 ? 'file' : 'files'}` + (size ? `, ${size}` : ''),
    smallChk: 'Makes sure the files arrived whole',
    smallBak: 'Copies of the files that get replaced',
    smallIns: 'Other mods are left alone',

    phaseWait: 'Waiting for the game to close',
    phaseDl: 'Downloading',
    phaseChk: 'Checking SHA-256',
    phaseBak: 'Taking a backup',
    phaseIns: 'Installing',
    phaseDone: 'All installed',
    subWait: 'Starts once the game has closed',
    subChk: (i, n, item) => `${i} of ${n}: ${item}`,
    subBak: 'Files that get replaced',

    // the countdown card
    cdTitle: 'Starting the game',
    cdTitleAgain: 'Starting the game again as it was',
    cdLeft: (n) => `Starting in ${n}`,
    notNow: "Don't start it now",

    // 3: failed
    failTitle: {
      offline: () => "Couldn't reach GitHub",
      busy: () => "GitHub didn't answer, or the download broke off",
      limited: () => 'GitHub is limiting downloads right now',
      notfound: (mod) => (mod ? `${mod} isn't on GitHub any more` : "The update isn't on GitHub any more"),
      mismatch: (mod) => (mod ? `The download of ${mod} doesn't match the release` : "The download doesn't match the release"),
      install: () => 'Something went wrong while copying',
      running: () => 'The game is still running',
      otherloader: () => 'Another mod loader is in the game folder',
      other: () => "Couldn't update",
    },
    failAdvice: {
      offline: () => "Check that you're online and try again.",
      busy: () => 'Try again in a little while.',
      limited: (min) => (min > 0 ? `Try again in ${min} ${min === 1 ? 'minute' : 'minutes'}.` : 'Try again a little later.'),
      notfound: () => "Please tell the mod's author.",
      mismatch: () => "The size or SHA-256 was different, so the file wasn't used. Try again.",
      install: () => 'Try again.',
      running: () => 'Close the game, then try again.',
      otherloader: () => 'Only one mod loader can be used at a time, so nothing was installed.',
      other: () => 'The details below say what happened. Try again.',
    },
    failPlayLater: 'Or just play now and update later.',
    failRestart: 'The game starts again as it was. You can update later from the Mods screen.',
    folderSame: 'Your game folder is just as it was (nothing was changed)',
    folderRestored: (n) => `Your game folder is just as it was (put back as it was, ${n} ${n === 1 ? 'file' : 'files'})`,
    folderPartly: "Some files couldn't be put back. The log says which.",
    alreadyUpdated: (names) => `Already updated before this: ${joinNames(names, 'en')}`,
    details: 'Details',
    openLog: 'Open the log folder',
    copyDetails: 'Copy details',
    copied: 'Copied',
    tryAgain: 'Try again',
  },

  ja: {
    minimize: '最小化',
    close: '閉じる',

    stateList: '更新があります',
    stateUpdating: '更新しています',
    stateRestart: '更新して再起動',
    stateFailed: '更新できませんでした',

    introChecking: '更新があるか確認しています',
    introFound: '更新がありました',
    introNone: '更新はありません',
    introStarting: 'ゲームを起動しています',

    listTitle: (n) => `${n} つの Mod に更新があります`,
    listLead: (when) => '入れるものを選んでください。前回ゲームの中で調べた結果です' + (when ? `（${when}）。` : '。'),
    installMod: (name) => `${name} を入れる`,
    cantHere: 'ここからは入れられません',
    openRelease: 'リリースページを開く',
    selected: (n, size) => `選んだもの：${n} つ` + (size ? `（${size}）` : ''),
    whatsNew: '変更点',
    notesLabel: '変更点',
    skipVersion: 'このバージョンは知らせない',
    cantNote: 'この Mod はここからは更新できません。リリースページから入れてください。',
    notesNone: 'このバージョンの変更点は書かれていません。',
    notesCut: '（途中までです。続きはリリースページで見られます）',
    netList: '「更新して起動」を押したときだけ、github.com と release-assets.githubusercontent.com につなぎます。押さなければ、どこにもつなぎません。',
    playNoUpdate: 'このまま起動',
    updateAndPlay: '更新して起動',

    updTitle: '更新しています',
    updLead: (items) => (items.length > 3
      ? `${items.length} つの更新を入れて、そのままゲームを起動します。`
      : `${joinNames(items, 'ja')} を入れて、そのままゲームを起動します。`),
    restartTitle: 'ゲームを閉じました。更新しています',
    restartLead: '終わったら、ゲームを自動でもう一度起動します。',
    netUpdating: '確認が終わるまで、ゲームのフォルダーには何も書きません。キャンセルしても元のままです。',
    netRestart: (backup) => `控え：${backup} · つなぐのは github.com と release-assets.githubusercontent.com だけです。`,
    cancel: 'キャンセル',
    logLabel: 'インストールのログ',

    stepWait: 'ゲームが閉じるのを待つ',
    stepDl: 'ダウンロード',
    stepChk: 'サイズと SHA-256 の確認',
    stepBak: 'バックアップ',
    stepIns: '入れる',
    stepGo: 'ゲームを起動',
    stepGoAgain: 'ゲームをもう一度起動',
    smallDl: (n, size) => `${n} つ` + (size ? `、${size}` : ''),
    smallChk: '届いたファイルが壊れていないか見ます',
    smallBak: '上書きするファイルを控えておきます',
    smallIns: 'ほかの Mod には触れません',

    phaseWait: 'ゲームが閉じるのを待っています',
    phaseDl: 'ダウンロードしています',
    phaseChk: 'SHA-256 を確認しています',
    phaseBak: 'バックアップを取っています',
    phaseIns: '入れています',
    phaseDone: '入れ終わりました',
    subWait: 'ゲームが閉じたら始めます',
    subChk: (i, n, item) => `${i} つめ：${item}`,
    subBak: '上書きするファイル',

    cdTitle: 'まもなくゲームを起動します',
    cdTitleAgain: '元のままゲームをもう一度起動します',
    cdLeft: (n) => `あと ${n} 秒`,
    notNow: '今は起動しない',

    failTitle: {
      offline: () => 'GitHub に接続できませんでした',
      busy: () => 'GitHub から返事がないか、ダウンロードが途中で切れました',
      limited: () => 'GitHub の利用制限にかかりました',
      notfound: (mod) => (mod ? `${mod} が GitHub に見つかりません` : '更新が GitHub に見つかりません'),
      mismatch: (mod) => (mod ? `${mod} のダウンロードが、リリースに書かれたものと合いません` : 'ダウンロードしたファイルが、リリースに書かれたものと合いません'),
      install: () => '入れている途中で失敗しました',
      running: () => 'ゲームがまだ動いています',
      otherloader: () => 'ゲームのフォルダーに、別の Mod ローダーが入っています',
      other: () => '更新できませんでした',
    },
    failAdvice: {
      offline: () => 'インターネットにつながっているか確かめて、もう一度試してください。',
      busy: () => '少し待ってから、もう一度試してください。',
      limited: (min) => (min > 0 ? `${min} 分後にもう一度試してください。` : 'しばらくしてから、もう一度試してください。'),
      notfound: () => 'Mod の作者に知らせてください。',
      mismatch: () => 'サイズか SHA-256 が違ったので、そのファイルは使っていません。もう一度試してください。',
      install: () => 'もう一度試してください。',
      running: () => 'ゲームを閉じてから、もう一度試してください。',
      otherloader: () => 'Mod ローダーは 1 つしか使えないので、何も入れていません。',
      other: () => '何が起きたかは下の詳細にあります。もう一度試してください。',
    },
    failPlayLater: 'このまま起動して、あとで更新することもできます。',
    failRestart: 'ゲームは元のままもう一度起動します。更新はあとで Mods 画面からできます。',
    folderSame: 'ゲームのフォルダーは元のままです（何も変えていません）',
    folderRestored: (n) => `ゲームのフォルダーは元のままです（巻き戻しました、${n} ファイル）`,
    folderPartly: '一部のファイルを元に戻せませんでした。どれかはログにあります。',
    alreadyUpdated: (names) => `この前に更新が済んだもの：${joinNames(names, 'ja')}`,
    details: '詳細',
    openLog: 'ログのフォルダーを開く',
    copyDetails: '詳細をコピー',
    copied: 'コピーしました',
    tryAgain: 'もう一度',
  },
};
