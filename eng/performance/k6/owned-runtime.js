import http from 'k6/http';
import { check, sleep } from 'k6';
import { sha256 } from 'k6/crypto';
import { Counter, Rate } from 'k6/metrics';

// The C# owner supplies an already admitted AppHost origin and disposable output path.
const origin = __ENV.LOAD_ORIGIN;
const target = __ENV.LOAD_TARGET;
const profile = __ENV.LOAD_PROFILE;
const journey = __ENV.LOAD_JOURNEY;
const budget = budgetFor(profile);
const mode = budget.rate === 0 ? 'closed' : 'open';
const digest = __ENV.LOAD_DIGEST;
const runId = __ENV.LOAD_RUN_ID;
const tenant = __ENV.LOAD_TENANT;
const readId = __ENV.LOAD_READ_ID;
const writeId = __ENV.LOAD_WRITE_ID;
const guid = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/;
const eventJourney = ['event-read', 'event-search', 'event-write'].includes(journey);
const expectedPath = journey === 'event-read' ? `/api/Event/${readId}`
    : journey === 'event-search' ? `/api/Event?pageSize=20&searchTerm=${runId}` : `/api/Event/${writeId}`;
if (!/^http:\/\/127\.0\.0\.1:[1-9][0-9]*$/.test(origin) || !__ENV.LOAD_SUMMARY ||
    (journey === 'public-language'
        ? target !== `${origin}/api/language` || !/^[a-f0-9]{64}$/.test(digest) ||
          !['closed-lookup', 'open-lookup'].includes(profile)
        : !eventJourney || !/^[a-f0-9]{32}$/.test(runId) || tenant !== `load-${runId}` ||
          !guid.test(readId) || !guid.test(writeId) || readId === writeId || target !== origin + expectedPath ||
          (journey === 'event-write' ? profile !== 'constrained-write' || !__ENV.LOAD_TOKEN
              : ['closed-lookup', 'open-lookup', 'constrained-write'].includes(profile)))) {
    throw new Error('load_configuration_not_admitted');
}

function budgetFor(name) {
    switch (name) {
        case 'closed-lookup': return { duration: 5, rate: 0, maxVUs: 4 };
        case 'open-lookup': return { duration: 5, rate: 8, maxVUs: 4 };
        case 'low-rate': return { duration: 10, rate: 1, maxVUs: 4 };
        case 'steady': return { duration: 30, rate: 4, maxVUs: 4 };
        case 'spike': return { duration: 5, rate: 8, maxVUs: 4 };
        case 'recovery': return { duration: 10, rate: 1, maxVUs: 4 };
        case 'soak': return { duration: 120, rate: 2, maxVUs: 4 };
        case 'constrained-write': return { duration: 20, rate: 1, maxVUs: 1 };
        default: throw new Error('load_profile_not_admitted');
    }
}

const attempts = new Counter('load_attempts');
const responses = new Counter('load_responses');
const correct = new Counter('load_correct');
const correctness = new Rate('load_correctness');
const thresholds = {
    load_correctness: ['rate==1'],
    checks: ['rate==1'],
    http_req_failed: ['rate==0'],
    load_responses: ['count>0'],
};
if (mode === 'open') thresholds.dropped_iterations = ['count==0'];

export const options = {
    scenarios: {
        owned: mode === 'closed'
            ? { executor: 'constant-vus', vus: budget.maxVUs, duration: `${budget.duration}s`, gracefulStop: '5s' }
            : { executor: 'constant-arrival-rate', rate: budget.rate, timeUnit: '1s', duration: `${budget.duration}s`,
                preAllocatedVUs: budget.maxVUs, maxVUs: budget.maxVUs, gracefulStop: '5s' },
    },
    thresholds,
    maxRedirects: 0,
    summaryTrendStats: ['p(50)', 'p(95)', 'p(99)'],
    systemTags: ['name', 'method'],
};

function receive(response) {
    responses.add(response.status > 0 ? 1 : 0);
    if (response.status !== 200 || !/^application\/json(?:;|$)/i.test(response.headers['Content-Type'] || '') ||
        typeof response.body !== 'string' || response.body.length > 4 * 1024 * 1024) return null;
    try { return JSON.parse(response.body); } catch (_) { return null; }
}

function parameters(name, authenticated) {
    const headers = { Accept: 'application/json' };
    if (eventJourney) headers['X-Tenant-Slug'] = tenant;
    if (authenticated) headers.Authorization = `Bearer ${__ENV.LOAD_TOKEN}`;
    return { timeout: '2s', redirects: 0, tags: { name }, headers };
}

let writeSequence = 0;

export default function () {
    attempts.add(1);
    let valid = false;
    if (journey === 'public-language') {
        const response = http.get(target, parameters('public-language-lookup', false));
        const body = receive(response);
        valid = body !== null && typeof response.body === 'string' && sha256(response.body, 'hex') === digest;
    } else if (journey === 'event-write') {
        writeSequence += 1;
        const before = receive(http.get(`${target}/management-detail`, parameters('owned-write-before', true)));
        if (writeSequence <= 20 && before && before.id === writeId && guid.test(before.concurrencyStamp)) {
            const title = `Load serial ${runId}-${String(writeSequence).padStart(4, '0')}`;
            const patch = parameters('owned-title-patch', true);
            patch.headers['Content-Type'] = 'application/json';
            patch.headers['If-Match'] = `"${before.concurrencyStamp}"`;
            patch.headers['Idempotency-Key'] = `${runId}-write-${writeSequence}`;
            const changed = receive(http.patch(target, JSON.stringify({ title: { value: title } }), patch));
            const after = receive(http.get(`${target}/management-detail`, parameters('owned-write-after', true)));
            valid = changed !== null && after && after.id === writeId && after.title === title &&
                guid.test(after.concurrencyStamp) && after.concurrencyStamp !== before.concurrencyStamp;
        }
    } else {
        const body = receive(http.get(target, parameters(journey, false)));
        if (journey === 'event-read') {
            valid = body && body.id === readId && body.title === `Load search ${runId}`;
        } else {
            const items = body && body._embedded && body._embedded.items;
            valid = Array.isArray(items) && items.length === 1 && items[0].event &&
                items[0].event.id === readId && items[0].event.title === `Load search ${runId}`;
        }
    }
    valid = valid === true;
    correct.add(valid ? 1 : 0);
    correctness.add(valid);
    check(valid, { 'owned journey preserves expected state': value => value });
    if (mode === 'closed') sleep(0.1);
}

export function handleSummary(data) {
    function value(name, statistic) {
        const metric = data.metrics[name];
        if (!metric || !Number.isFinite(metric.values[statistic])) {
            throw new Error('load_required_metric_missing');
        }
        return metric.values[statistic];
    }
    const summary = {
        schema: 2, journey, profile, mode,
        attempts: value('load_attempts', 'count'),
        responses: value('load_responses', 'count'),
        correct: value('load_correct', 'count'),
        iterations: value('iterations', 'count'),
        httpRequests: value('http_reqs', 'count'),
        correctnessRate: value('load_correctness', 'rate'),
        httpFailureRate: value('http_req_failed', 'rate'),
        dropped: mode === 'open' ? value('dropped_iterations', 'count') : 0,
        p50Milliseconds: value('http_req_duration', 'p(50)'),
        p95Milliseconds: value('http_req_duration', 'p(95)'),
        p99Milliseconds: value('http_req_duration', 'p(99)'),
    };
    return { [__ENV.LOAD_SUMMARY]: JSON.stringify(summary) };
}
