using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.WSA.Input;

public class AnnotationsManager : MonoBehaviour
{
    public TextMesh m_tmDebug;
    public GameObject m_goAnnotationArrow;
    public GameObject m_goAnnotationNoEntry;
    private bool m_newComputation;
    private float m_relativePosX;
    private float m_relativePosY;
    private List<GameObject> m_annotations;
    private int m_iAnnotationSelected;
    private bool m_bGuidanceArrowEnabled;
    public GameObject m_goArrowGuidance;
    public TextMesh m_tmChat;
    private string m_sChat;

    private GestureRecognizer m_gestureRecognizer;
    private bool m_gestureToManage;
    private string m_sDebugText;
    private Ray m_ray;
    private bool m_rayShow;
    public LineRenderer m_lr;
    public GameObject m_debugCube;
    public UDPListener m_udpListener;
    private double m_dTimer;
    private bool m_bTimerEnabled;

    private int m_iDebugStatus;

    // Start is called before the first frame update
    void Start()
    {
        m_relativePosX = (float)0.95;
        m_relativePosY = (float)0.95;
        m_newComputation = true;
        m_iAnnotationSelected = 0;
        m_annotations = new List<GameObject>();
        m_gestureToManage = false;
        m_bGuidanceArrowEnabled = false;
        m_sDebugText = "";
        m_rayShow = false;
        m_dTimer = 10.0;
        m_bTimerEnabled = false;
        m_sChat = "";

        m_gestureRecognizer = new GestureRecognizer();
        m_gestureRecognizer.Tapped += (args) =>
        {
            Debug.Log("[AnnotationsManager::Start] Gesture tapped detected");
            m_gestureToManage = true;
        };
        m_gestureRecognizer.StartCapturingGestures();

        m_iDebugStatus = 0;
    }

    // Update is called once per frame
    void Update()
    {
        RaycastHit hit;

        if (m_newComputation)
        {
            m_sDebugText = "[TestRaycast::Update] New computation ongoing x=" + m_relativePosX + " y=" + m_relativePosY;
            m_newComputation = false;

            Vector3 userClickedPointTR = Camera.main.ScreenToWorldPoint(new Vector3((int)(Camera.main.pixelWidth * m_relativePosX), (int)(Camera.main.pixelHeight * m_relativePosY), Camera.main.nearClipPlane));
            Vector3 directionTR = Camera.main.transform.forward;

            Ray ray = Camera.main.ViewportPointToRay(new Vector3(m_relativePosX, m_relativePosY,0));
            m_ray = ray;
            m_rayShow = true;
            
            if (Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity))
            {
                m_sDebugText = "[TestRaycast::Update] What has been hit: " + hit.collider.gameObject.tag;
                if (hit.collider.gameObject.tag == "Annotations")
                {
                    for ( int i = 0; i < m_annotations.Count; i ++)
                    {
                        if ( m_annotations[i] == hit.collider.gameObject)
                        {
                            Destroy(m_annotations[i]);
                            m_annotations.RemoveAt(i);
                            m_sDebugText = "[TestRaycast::Update] Object removed!";
                        }
                    }
                }
                else
                {
                    bool newAnnotationAdded = false;
                    if (m_iAnnotationSelected == 0)
                    {
                        m_annotations.Add(Instantiate(m_goAnnotationArrow));
                        newAnnotationAdded = true;
                    }
                    else if (m_iAnnotationSelected == 1)
                    {
                        m_annotations.Add(Instantiate(m_goAnnotationNoEntry));
                        newAnnotationAdded = true;
                    }

                    if (newAnnotationAdded)
                    {
                        m_annotations[m_annotations.Count - 1].transform.SetPositionAndRotation(hit.point, Quaternion.FromToRotation(Vector3.up, hit.normal));
                        m_annotations[m_annotations.Count - 1].SetActive(true);
                    }
                }
            }
            else
            {
                m_udpListener.sendUdpMessage("WARNING noHits");
            }
        }
        else if (m_gestureToManage)
        {
            Debug.Log("[AnnotationsManager::Update] Gesture detected");
            m_sDebugText = "[TestRaycast::Update] Handling gesture";
            m_gestureToManage = false;
            var headPosition = Camera.main.transform.position;
            var gazeDirection = Camera.main.transform.forward;

            RaycastHit hitInfo;
            if (Physics.Raycast(headPosition, gazeDirection, out hitInfo))
            {
                for (int i = 0; i < m_annotations.Count; i++)
                {
                    if (m_annotations[i] == hitInfo.collider.gameObject)
                    {
                        Destroy(m_annotations[i]);
                        m_annotations.RemoveAt(i);
                        m_sDebugText = "[TestRaycast::Update] Object removed by user!";
                    }
                }
            }
        }

        m_goArrowGuidance.SetActive(m_bGuidanceArrowEnabled);


        if (m_iDebugStatus == 0)
        {
            m_debugCube.SetActive(false);
            m_lr.gameObject.SetActive(false);
            m_tmDebug.gameObject.SetActive(false);
        }
        else if (m_iDebugStatus == 3)
        {
            m_debugCube.SetActive(true);
            m_lr.gameObject.SetActive(true);
            m_tmDebug.gameObject.SetActive(true);

            if (m_rayShow)
            {
                Vector3 endPosition = m_ray.origin + (10 * m_ray.direction);

                m_lr.SetPosition(0, m_ray.origin);
                m_lr.SetPosition(1, endPosition);
                m_lr.SetColors(Color.green, Color.green);
                m_debugCube.transform.SetPositionAndRotation(m_ray.origin, m_debugCube.transform.rotation);

                m_sDebugText = "Camera resolution: " + Camera.main.pixelWidth + " " + Camera.main.pixelHeight;
            }

            m_tmDebug.text = m_sDebugText;
        }

        // Handling timer
        if (m_bTimerEnabled)
        {
            m_tmChat.gameObject.SetActive(true);
            m_dTimer -= Time.deltaTime;
            if (m_dTimer <= 0.0)
            {
                m_bTimerEnabled = false;
                m_tmChat.gameObject.SetActive(false);
                m_sChat = "";
            }
        }

        m_tmChat.text = m_sChat;
    }

    public void addAnnotationRelativePos(double x, double y)
    {
        m_relativePosX = (float)x;
        m_relativePosY = (float)y;
        m_newComputation = true;
    }

    public void addAnnotationMessage(string message)
    {
        m_sChat = message;
        
        m_dTimer = 10.0f;
        m_bTimerEnabled = true;
    }

    public void changeAnnotation(int newAnnotation)
    {
        m_iAnnotationSelected = newAnnotation;
        m_sDebugText = "[AnnotationsManager::changeAnnotation] Annotation type changed: " + m_iAnnotationSelected;
    }

    public void enableGuidanceArrow(bool enable)
    {
        m_bGuidanceArrowEnabled = enable;
    }

    public void processDebug(int level)
    {
        if ( level == 0)
        {
            m_iDebugStatus = 0;
        }
        else if (level == 3)
        {
            m_iDebugStatus = 3;
        }
    }
}