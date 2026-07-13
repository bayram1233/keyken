const SECRET = 'FendrSystemCare2025!';
const PREFIX = 'FENDR';
const AD_SECONDS = 30;

let visitedAds = new Set();
let countdown = AD_SECONDS;
let timerInterval = null;

function computeChecksum(body) {
    const data = new TextEncoder().encode(SECRET + body);
    return crypto.subtle.digest('SHA-256', data).then(buf => {
        const hex = Array.from(new Uint8Array(buf)).map(b => b.toString(16).padStart(2, '0')).join('');
        return hex.substring(0, 4).toUpperCase();
    });
}

function generateKey() {
    const bytes = new Uint8Array(4);
    crypto.getRandomValues(bytes);
    const body = Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('').toUpperCase();
    return computeChecksum(body).then(check => `${PREFIX}-${body}-${check}`);
}

function startTimer() {
    document.getElementById('countdown').textContent = countdown;
    timerInterval = setInterval(() => {
        countdown--;
        document.getElementById('countdown').textContent = countdown;
        if (countdown <= 0) {
            clearInterval(timerInterval);
            document.getElementById('btn-continue').disabled = false;
            document.querySelector('.timer').innerHTML = '<span style="color:#30d158">Süre doldu! Devam edebilirsiniz.</span>';
        }
    }, 1000);
}

document.querySelectorAll('.ad-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        visitedAds.add(btn.dataset.ad);
        btn.classList.add('visited');
        if (visitedAds.size >= 1 && !timerInterval) startTimer();
    });
});

document.getElementById('btn-continue').addEventListener('click', async () => {
    const key = await generateKey();
    document.getElementById('api-key').textContent = key;
    document.getElementById('step-ads').classList.add('hidden');
    document.getElementById('step-key').classList.remove('hidden');
});

document.getElementById('btn-copy').addEventListener('click', () => {
    const key = document.getElementById('api-key').textContent;
    navigator.clipboard.writeText(key).then(() => {
        document.getElementById('btn-copy').textContent = 'Kopyalandı!';
        setTimeout(() => document.getElementById('btn-copy').textContent = 'Kopyala', 2000);
    });
});
