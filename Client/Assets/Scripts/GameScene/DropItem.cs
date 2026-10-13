using System.Collections;
using UnityEngine;

public class DropItem : MonoBehaviour
{
    [Header("자석 효과 설정")]
    public float popForce = 5f;       // 위로 튀어오르는 힘
    public float waitBeforeMagnet = 0.5f; // 날아가기 전 대기 시간
    public float flySpeed = 10f;      // 플레이어에게 날아가는 속도

    private Transform playerTransform;
    private Rigidbody2D rb;
    private bool isFlyingToPlayer = false;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        // 태그를 이용해 씬에 있는 플레이어를 찾음 ("Player" 태그가 붙어있어야 함)
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }

        // 1. 위쪽 + 약간의 랜덤 좌우 방향으로 톡 튀어오르기
        if (rb != null)
        {
            float randomX = Random.Range(-0.5f, 0.5f);
            Vector2 forceDir = new Vector2(randomX, 1f).normalized;
            rb.AddForce(forceDir * popForce, ForceMode2D.Impulse);
        }

        // 2. 일정 시간 뒤 플레이어에게 날아가기 시작
        StartCoroutine(StartMagnetEffect());
    }

    private IEnumerator StartMagnetEffect()
    {
        yield return new WaitForSeconds(waitBeforeMagnet);

        // 중력 영향을 없애서 직선으로 날아갈 수 있게 함
        if (rb != null) rb.gravityScale = 0f;

        isFlyingToPlayer = true;
    }

    private void Update()
    {
        if (isFlyingToPlayer && playerTransform != null)
        {
            // 플레이어의 위치로 빠르게 이동
            transform.position = Vector3.MoveTowards(transform.position, playerTransform.position, flySpeed * Time.deltaTime);

            // 플레이어와 아주 가까워지면 획득 처리
            if (Vector3.Distance(transform.position, playerTransform.position) < 0.5f)
            {
                GetItem();
            }
        }
    }

    private void GetItem()
    {
        // 획득 UI 띄우기 (별도 UI 매니저 스크립트 필요)
        Debug.Log("스펠 획득!");

        // 반짝이 파괴
        Destroy(gameObject);
    }
}